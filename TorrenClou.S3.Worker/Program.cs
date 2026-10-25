using Serilog;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Interfaces;
using TorrenClou.Core.Interfaces.Hangfire;
using TorrenClou.Core.Options;
using TorrenClou.Infrastructure.Extensions;
using TorrenClou.Infrastructure.Services;
using TorrenClou.Infrastructure.Services.Handlers;
using TorrenClou.Infrastructure.Services.Redis;
using TorrenClou.S3.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

const string ServiceName = "torrenclou-s3-worker";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog();

    Log.Information("Starting {ServiceName}", ServiceName);

    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorrenClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    builder.Services.AddSharedHangfireBase(builder.Configuration);
    builder.Services.AddSharedHangfireServer(builder.Configuration, queues: ["s3", "default"]);

    builder.Services.AddSingleton<IJobCancellationSignal, RedisJobCancellationSignal>();
    builder.Services.AddScoped<IJobHandlerFactory, JobHandlerFactory>();
    builder.Services.AddScoped<IS3UploadJob, S3UploadJob>();

    builder.Services.AddScoped<IJobStatusService, JobStatusService>();
    builder.Services.AddScoped<IUserSettingsService, TorrenClou.Application.Services.UserSettingsService>();
    builder.Services.AddScoped<IDownloadCleanupService, DownloadCleanupService>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();

    builder.Services.AddStorageRoutingServices(builder.Configuration);

    builder.Services.Configure<JobHealthMonitorOptions>(opts =>
    {
        opts.CheckInterval = TimeSpan.FromMinutes(2);
        opts.StaleJobThreshold = TimeSpan.FromMinutes(5);
    });
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();

    builder.Services.Configure<HostOptions>(opts =>
    {
        opts.ShutdownTimeout = TimeSpan.FromMinutes(6);
    });

    var host = builder.Build();

    Log.Information("{ServiceName} started successfully", ServiceName);
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "S3 Worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}