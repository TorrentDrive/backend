using Serilog;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Interfaces;
using TorrenClou.Core.Interfaces.Hangfire;
using TorrenClou.Core.Options;
using TorrenClou.Infrastructure.Extensions;
using TorrenClou.Infrastructure.Services;
using TorrenClou.Infrastructure.Services.Drive;
using TorrenClou.Infrastructure.Settings;
using TorrenClou.GoogleDrive.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

const string ServiceName = "torrenclou-googledrive-worker";

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
    builder.Services.AddSharedHangfireServer(builder.Configuration, queues: ["googledrive", "default"]);

    // Worker Services (Google Drive credentials configured per-user via API)
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<IGoogleDriveUploadJob, GoogleDriveUploadJob>();
    builder.Services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
    builder.Services.AddScoped<IUploadProgressContext, UploadProgressContext>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();
    builder.Services.AddScoped<IJobStatusService, TorrenClou.Infrastructure.Services.JobStatusService>();
    // DownloadCleanupService reads the delete-after-upload preference from the DB.
    builder.Services.AddScoped<IUserSettingsService, TorrenClou.Application.Services.UserSettingsService>();
    builder.Services.AddScoped<IDownloadCleanupService, DownloadCleanupService>();

    // Storage connection health + upload failover routing
    builder.Services.AddStorageRoutingServices(builder.Configuration);

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => opts.CheckInterval = TimeSpan.FromMinutes(2));
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();

    // Configure host shutdown timeout to allow Hangfire graceful shutdown
    builder.Services.Configure<HostOptions>(opts => 
    {
        opts.ShutdownTimeout = TimeSpan.FromMinutes(6); // Longer than Hangfire's ServerTimeout
    });

    var host = builder.Build();
    
    Log.Information("{ServiceName} started successfully", ServiceName);
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Google Drive Worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
