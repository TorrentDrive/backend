using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Infrastructure.Services;
using TorrentDrive.Application.Services.Google_Drive;
using TorrentDrive.Application.Services.Setup;
using TorrentDrive.Infrastructure.Services.Drive;
using TorrentDrive.Infrastructure.Services.Handlers;
using TorrentDrive.Infrastructure.Services.Health;
using TorrentDrive.Infrastructure.Services.Transfer;
using TorrentDrive.Infrastructure.Services.Redis;
using TorrentDrive.Core.Options;

namespace TorrentDrive.Infrastructure.Extensions
{
    /// <summary>
    /// Registers API-specific infrastructure services.
    /// Database, Redis, and Repository registrations are handled by
    /// AddSharedDatabase() and AddSharedRedis() in SharedConfigurationExtensions.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<ITokenService, TokenService>();

            // Google Drive Services (credentials configured per-user via API)
            services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
            services.AddScoped<IGoogleDriveService, GoogleDriveService>();

            // Upload Progress Context (scoped per Hangfire job)
            services.AddScoped<IUploadProgressContext, UploadProgressContext>();

            // Transfer Speed Metrics (singleton for metrics collection)
            services.AddSingleton<ITransferSpeedMetrics, TransferSpeedMetrics>();

            // Job Status Service (timeline tracking)
            services.AddScoped<IJobStatusService, JobStatusService>();

            // Health Check Service
            services.AddScoped<IHealthCheckService, HealthCheckService>();

            // Downloads volume inspection and purge (API mounts the same volume)
            services.AddScoped<IDownloadMaintenanceService, DownloadMaintenanceService>();

            // Storage connection health + upload failover routing
            services.AddStorageRoutingServices(configuration);

            // Google API Client (for OAuth token exchange and user info)
            services.AddScoped<IGoogleApiClient, GoogleApiClient>();

            // Job Handlers (Strategy Pattern for decoupled job processing)
            // Storage Provider Handlers
            services.AddScoped<IStorageProviderHandler, GoogleDriveStorageProviderHandler>();
            services.AddScoped<IStorageProviderHandler, S3StorageProviderHandler>();
            
            // Job Type Handlers
            services.AddScoped<IJobTypeHandler, TorrentJobTypeHandler>();
            
            // Job Cancellation Handlers
            services.AddScoped<IJobCancellationHandler, TorrentCancellationHandler>();
            
            // Job Handler Factory
            services.AddScoped<IJobHandlerFactory, JobHandlerFactory>();

            // Distributed cancellation signal (Redis-backed, singleton — stateless Redis wrapper)
            services.AddSingleton<IJobCancellationSignal, RedisJobCancellationSignal>();

            return services;
        }

        /// <summary>
        /// Registers storage connection health probing and upload failover routing.
        /// Called by the API through <see cref="AddInfrastructureServices"/>, and directly
        /// by the upload workers, which build their own service collections.
        /// </summary>
        public static IServiceCollection AddStorageRoutingServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Configuration first, then the database on top: a value saved in the Settings
            // tab overrides the same value supplied as an environment variable.
            services.Configure<UploadRoutingOptions>(
                configuration.GetSection(UploadRoutingOptions.SectionName));

            services.AddScoped<IConfigureOptions<UploadRoutingOptions>, SystemSettingsRoutingOptions>();
            services.AddHostedService<SystemSettingsRefresher>();

            // The settings row is what the override above reads. Registered here rather than
            // per-host so every process that routes uploads has it: the API gets it from
            // AddApplicationServices, the two upload workers only from this call.
            services.TryAddSingleton<SystemSettingsCache>();
            services.TryAddScoped<ISystemSettingsService, SystemSettingsService>();

            // Probe dependencies. TryAdd keeps this safe in hosts (the API, the Drive
            // worker) that already register them; the S3 worker gets them from here.
            services.AddHttpClient();
            services.TryAddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
            services.TryAddScoped<IUploadProgressContext, UploadProgressContext>();

            // Probes are resolved by ProviderType inside StorageProfileHealthService.
            services.AddScoped<IStorageHealthProbe, GoogleDriveHealthProbe>();
            services.AddScoped<IStorageHealthProbe, S3HealthProbe>();

            services.AddScoped<IStorageProfileHealthService, StorageProfileHealthService>();
            services.AddScoped<IUploadRoutingService, UploadRoutingService>();
            services.TryAddScoped<IS3JobService, S3JobService>();
            services.AddScoped<IStorageDestination, GoogleDriveStorageDestination>();
            services.AddScoped<IStorageDestination, S3StorageDestination>();
            services.TryAddScoped<IJobExecutionTracker, JobExecutionTracker>();
            services.AddScoped<IStorageTransferOrchestrator, StorageTransferOrchestrator>();

            return services;
        }
    }
}
