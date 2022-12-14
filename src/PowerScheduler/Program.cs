using PowerScheduler;
using PowerScheduler.Data;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var loggerConfiguration = new LoggerConfiguration()
#if DEBUG
    .MinimumLevel.Debug()
#else
    .MinimumLevel.Information()
#endif
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Orleans", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Async(c => c.File(new CompactJsonFormatter(), "Logs/logs.txt", rollingInterval: RollingInterval.Day))
    .WriteTo.Async(c => c.Console());

if (IsMigrateDatabase(args))
{
    loggerConfiguration.MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning);
    loggerConfiguration.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);
}

Log.Logger = loggerConfiguration.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Orleans.Clustering.AdoNet.Storage.AdoNetInvariants
    string invariant = "Microsoft.Data.SqlClient";
    string connectionString = builder.Configuration.GetConnectionString("Default");

    builder.Host.AddAppSettingsSecretsJson()
        .UseAutofac()
        .UseSerilog()
        .UseOrleans((context, builder) =>
        {
            builder
                .UseAdoNetClustering(options =>
                {
                    options.Invariant = invariant;
                    options.ConnectionString = connectionString;
                })
                .UseAdoNetReminderService(options =>
                {
                    options.Invariant = invariant;
                    options.ConnectionString = connectionString;
                })
                .UsePowerScheduler();
        });

    await builder.AddApplicationAsync<PowerSchedulerModule>();
    var app = builder.Build();
    await app.InitializeApplicationAsync();

    if (IsMigrateDatabase(args))
    {
        await app.Services.GetRequiredService<PowerSchedulerDbMigrationService>().MigrateAsync();
        return 0;
    }

    Log.Information("Starting PowerScheduler.");
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    if (ex.GetType().Name.Equals("StopTheHostException", StringComparison.Ordinal))
    {
        throw;
    }

    Log.Fatal(ex, "PowerScheduler terminated unexpectedly!");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static bool IsMigrateDatabase(string[] args)
{
    return args.Any(x => x.Contains("--migrate-database", StringComparison.OrdinalIgnoreCase));
}
