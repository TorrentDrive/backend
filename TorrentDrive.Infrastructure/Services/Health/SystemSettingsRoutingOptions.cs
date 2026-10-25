using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Options;

namespace TorrentDrive.Infrastructure.Services.Health
{
    /// <summary>
    /// Feeds the instance settings row into <see cref="UploadRoutingOptions"/>, so the
    /// routing services keep consuming options the ordinary way while the values behind
    /// them come from the Settings tab rather than from environment variables.
    ///
    /// Runs after the configuration binding registered alongside it, so a value stored in
    /// the database wins over the same value in configuration.
    /// </summary>
    public class SystemSettingsRoutingOptions(ISystemSettingsService systemSettings)
        : IConfigureOptions<UploadRoutingOptions>
    {
        public void Configure(UploadRoutingOptions options)
        {
            // Synchronous by contract: the options pipeline has no async entry point, so
            // this reads the cache that SystemSettingsRefresher keeps warm and never issues
            // a query of its own.
            var current = systemSettings.GetCachedRoutingOptions();

            options.EnableFailover = current.EnableFailover;
            options.MaxFailoverAttempts = current.MaxFailoverAttempts;
            options.FailureThreshold = current.FailureThreshold;
            options.HealthCacheTtl = current.HealthCacheTtl;
            options.QuotaHeadroomRatio = current.QuotaHeadroomRatio;
            options.DegradedFreeQuotaRatio = current.DegradedFreeQuotaRatio;
            options.ProbeTimeout = current.ProbeTimeout;
        }
    }

    /// <summary>
    /// Keeps the settings cache warm.
    ///
    /// The options pipeline cannot await, so something has to do the database read off the
    /// request path. This also creates the settings row on a cold start, which is what makes
    /// a container come up with no configuration at all.
    /// </summary>
    public class SystemSettingsRefresher(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemSettingsRefresher> logger) : BackgroundService
    {
        /// <summary>
        /// Comfortably shorter than the cache TTL, so a reader never finds it expired.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(20);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RefreshAsync(stoppingToken);

                try
                {
                    await Task.Delay(RefreshInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();

                await settings.GetRoutingOptionsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // The database may not be migrated yet on a first boot. Callers fall back to
                // the last known values, or to configuration, so this is never fatal.
                logger.LogDebug(ex, "Could not refresh system settings; using the last known values");
            }
        }
    }
}
