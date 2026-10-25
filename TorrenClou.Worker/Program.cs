using Hangfire;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Interfaces;
using TorrenClou.Core.Interfaces.Hangfire;
using TorrenClou.Core.Options;
using TorrenClou.Infrastructure.Extensions;
using TorrenClou.Infrastructure.Filters;
using TorrenClou.Infrastructure.Services;
using TorrenClou.Infrastructure.Services.Redis;
using TorrenClou.Worker.Services;

const string ServiceName = "torrenclou-worker";

// Bootstrap logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog();

    Log.Information("Starting {ServiceName}", ServiceName);

    // Infrastructure
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorrenClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // Hangfire
    builder.Services.AddSharedHangfireBase(builder.Configuration);
    builder.Services.AddSharedHangfireServer(builder.Configuration, queues: ["torrents", "default"]);

    // Worker Services
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IJobCancellationSignal, RedisJobCancellationSignal>();
    builder.Services.AddScoped<ITorrentDownloadJob, TorrentDownloadJob>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();
    builder.Services.AddScoped<IJobStatusService, JobStatusService>();
    builder.Services.AddSingleton<IJobRecoveryStrategy, TorrentRecoveryStrategy>();
    builder.Services.AddStorageRoutingServices(builder.Configuration);
    builder.Services.AddScoped<IStorageProviderHandler, TorrenClou.Infrastructure.Services.Handlers.GoogleDriveStorageProviderHandler>();
    builder.Services.AddScoped<IStorageProviderHandler, TorrenClou.Infrastructure.Services.Handlers.S3StorageProviderHandler>();
    builder.Services.AddScoped<IJobTypeHandler, TorrenClou.Infrastructure.Services.Handlers.TorrentJobTypeHandler>();
    builder.Services.AddScoped<IJobHandlerFactory, TorrenClou.Infrastructure.Services.Handlers.JobHandlerFactory>();

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => opts.CheckInterval = TimeSpan.FromMinutes(2));
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();

    // Configure host shutdown timeout to allow Hangfire graceful shutdown
    builder.Services.Configure<HostOptions>(opts => 
    {
        opts.ShutdownTimeout = TimeSpan.FromMinutes(6); // Longer than Hangfire's ServerTimeout
    });

    var host = builder.Build();

    // Registered against the real host container. Building a scratch provider here would
    // spin up a second DI graph and a duplicate, never-disposed Redis multiplexer.
    GlobalJobFilters.Filters.Add(new JobStateSyncFilter(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<JobStateSyncFilter>()
    ));

    Log.Information("{ServiceName} started successfully", ServiceName);
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
