using PowerScheduler.Entities;

namespace PowerScheduler.Runtime.Workers;

/// <summary>
/// 过滤连接超时的 Worker
/// </summary>
public class DisconnectedWorkerFilter : IWorkerFilter
{
    private readonly ILogger<DisconnectedWorkerFilter> _logger;

    public DisconnectedWorkerFilter(ILogger<DisconnectedWorkerFilter> logger)
    {
        _logger = logger;
    }

    public bool Filter(SchedulerWorker worker, SchedulerJob job)
    {
        var timeout = worker.Timeout();

        if (timeout)
        {
            _logger.LogInformation("[Job-{Job}] filter worker[{Worker}] due to timeout(lastActiveTime={Time})",
                job, worker, worker.LastActiveTime);
        }

        return timeout;
    }
}
