using PowerScheduler.Actors;
using PowerScheduler.Domain;
using PowerScheduler.Entities;
using PowerScheduler.Enums;
using Volo.Abp.Domain.Repositories;

namespace PowerScheduler.Runtime;

public class SchedulerTaskActor : ActorBase, ISchedulerTaskActor
{
    private readonly IRepository<SchedulerJob, Guid> _jobRepository;
    private readonly IRepository<SchedulerTask, Guid> _taskRepository;
    private readonly WorkerClusterManager _workerManager;
    private readonly SchedulerTaskManager _taskManager;
    private readonly HttpClient _httpClient;

    public SchedulerTaskActor(
        IRepository<SchedulerJob, Guid> jobRepository,
        IRepository<SchedulerTask, Guid> taskRepository,
        WorkerClusterManager workerManager,
        SchedulerTaskManager taskManager,
        HttpClient httpClient)
    {
        _jobRepository = jobRepository;
        _taskRepository = taskRepository;
        _workerManager = workerManager;
        _taskManager = taskManager;
        _httpClient = httpClient;
    }

    public Task DispatchTask(Guid taskId, TimeSpan dueTime)
    {
        Logger.LogInformation("received dispatch request, dispatch starts in {DueTime}s, taskId: {TaskId}", dueTime.TotalSeconds, taskId);

        RegisterTimer(DispatchCore, taskId, dueTime, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    private async Task DispatchCore(object state)
    {
        var taskId = (Guid)state;
        var schedulerTask = await _taskRepository.GetAsync(taskId);

        if (schedulerTask == null)
        {
            Logger.LogWarning("[Dispatcher-{TaskId}] cancel dispatch due to task has been deleted!", taskId);
            return;
        }

        var schedulerJob = await _jobRepository.FindAsync(schedulerTask.JobId);
        if (schedulerJob == null)
        {
            Logger.LogWarning("[Dispatcher-{JobId}|{TaskId}] cancel dispatch due to job has been deleted!", schedulerTask.JobId, taskId);
            return;
        }

        if (schedulerTask.TaskRunStatus == TaskRunStatus.Canceled)
        {
            Logger.LogInformation("[Dispatcher-{JobName}|{TaskId}] cancel dispatch due to task has been canceled", schedulerJob.Name, taskId);
            return;
        }

        if (schedulerTask.TaskRunStatus != TaskRunStatus.WaitingDispatch)
        {
            Logger.LogInformation("[Dispatcher-{JobName}|{TaskId}] cancel dispatch due to task has been dispatched", schedulerJob.Name, taskId);
            return;
        }

        if (schedulerJob.ProcessorInfo.IsNullOrEmpty())
        {
            Logger.LogWarning("[Dispatcher-{JobName}|{TaskId}] cancel dispatch due to ProcessorInfo is empty.", schedulerJob.Name, taskId);
            return;
        }

        Logger.LogInformation("[Dispatcher-{JobName}|{TaskId}] start to dispatch task.", schedulerJob.Name, taskId);

        // TODO: 并发限制

        // 获取当前最合适的 worker 列表
        var wokers = await _workerManager.GetSuitableWorkers(schedulerJob);
        if (wokers.IsNullOrEmpty())
        {
            Logger.LogWarning("[Dispatcher-{JobName}|{TaskId}] cancel dispatch job due to no worker available", schedulerJob.Name, taskId);

            await _taskManager.Update4TriggerFailed(taskId, TaskRunStatus.Failed, Clock.Now, Clock.Now, PowerSchedulerConsts.EmptyAddress, PowerSchedulerConsts.NoWorkerAvailable);

            await _taskManager.ProcessFinishedTask(taskId, TaskRunStatus.Failed, PowerSchedulerConsts.NoWorkerAvailable);

            return;
        }

        var workerIpList = wokers.Select(p => p.Address).ToList();
        var taskTrackerAddress = workerIpList.FirstOrDefault();

        await PostRequest(schedulerJob, schedulerTask, workerIpList);

        Logger.LogInformation("[Dispatcher-{JobName}|{TaskId}] send schedule request to TaskTracker[address:{}] successfully.", schedulerJob.Name, taskId, taskTrackerAddress);

        await _taskManager.Update4TriggerSucceed(taskId, TaskRunStatus.Succeed, Clock.Now, taskTrackerAddress);
    }

    /// <summary>
    /// 下发任务到Worker执行
    /// </summary>
    /// <param name="job"></param>
    /// <param name="task"></param>
    /// <param name="finalWorkersIpList"></param>
    /// <returns></returns>
    private async Task PostRequest(SchedulerJob job, SchedulerTask task, List<string> finalWorkersIpList)
    {
        // TODO: 发送请求
        var request = new HttpRequestMessage()
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(finalWorkersIpList.FirstOrDefault()),

        };

        await _httpClient.SendAsync(request);
    }
}
