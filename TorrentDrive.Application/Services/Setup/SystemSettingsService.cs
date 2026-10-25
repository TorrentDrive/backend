using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Settings;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Options;

namespace TorrentDrive.Application.Services.Setup
{
    public class SystemSettingsService(
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        SystemSettingsCache cache,
        ILogger<SystemSettingsService> logger) : ISystemSettingsService
    {
        public async Task<SystemSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
            => ToDto(await GetOrCreateAsync(cancellationToken));

        public async Task<SystemSettingsDto> UpdateSettingsAsync(
            UpdateSystemSettingsRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Validate(request);

            var settings = await GetOrCreateAsync(cancellationToken);

            settings.EnableFailover = request.EnableFailover;
            settings.MaxFailoverAttempts = request.MaxFailoverAttempts;
            settings.FailureThreshold = request.FailureThreshold;
            settings.HealthCacheTtlSeconds = request.HealthCacheTtlSeconds;
            settings.QuotaHeadroomRatio = request.QuotaHeadroomRatio;
            settings.DegradedFreeQuotaRatio = request.DegradedFreeQuotaRatio;
            settings.ProbeTimeoutSeconds = request.ProbeTimeoutSeconds;
            settings.HangfireWorkerCount = request.HangfireWorkerCount;
            settings.EnablePrometheus = request.EnablePrometheus;
            settings.EnableTracing = request.EnableTracing;

            await unitOfWork.Complete();

            // Only this process's cache is refreshed. The workers pick the change up when
            // their own copy expires, which is what the short TTL is for.
            cache.Store(ToRoutingOptions(settings));

            logger.LogInformation(
                "System settings updated | Failover: {Failover} | MaxAttempts: {MaxAttempts} | Workers: {Workers}",
                settings.EnableFailover, settings.MaxFailoverAttempts, settings.HangfireWorkerCount);

            return ToDto(settings);
        }

        public async Task<SystemSettings> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            var existing = await LoadAsync();
            if (existing != null) return existing;

            // First access on this instance. Seed from configuration so an install that was
            // tuned through environment variables keeps its tuning across the upgrade; after
            // this the row is authoritative and those variables are ignored.
            var settings = new SystemSettings
            {
                EnableFailover = ReadBool("UploadRouting:EnableFailover", true),
                MaxFailoverAttempts = ReadInt("UploadRouting:MaxFailoverAttempts", 3),
                FailureThreshold = ReadInt("UploadRouting:FailureThreshold", 3),
                HangfireWorkerCount = ReadInt("Hangfire:WorkerCount", 10),
                EnablePrometheus = ReadBool("Observability:EnablePrometheus", true),
                EnableTracing = ReadBool("Observability:EnableTracing", true)
            };

            unitOfWork.Repository<SystemSettings>().Add(settings);

            try
            {
                await unitOfWork.Complete();
                logger.LogInformation("Created system settings from configuration defaults");
            }
            catch (Exception ex)
            {
                // The API and three workers boot together, so two of them can reach here at
                // once. Whoever loses the race reads the winner's row instead of failing.
                logger.LogDebug(ex, "System settings row already created concurrently, re-reading");
                unitOfWork.Detach(settings);

                settings = await LoadAsync()
                    ?? throw new BusinessRuleException("SettingsUnavailable",
                        "Could not read or create the instance settings.");
            }

            return settings;
        }

        public async Task<UploadRoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken = default)
        {
            var cached = cache.Fresh;
            if (cached != null) return cached;

            var options = ToRoutingOptions(await GetOrCreateAsync(cancellationToken));
            cache.Store(options);
            return options;
        }

        public UploadRoutingOptions GetCachedRoutingOptions()
            // No database access by contract. A slightly stale copy beats a blocking read,
            // and the configuration defaults cover the first call before anything is cached.
            => cache.LastKnown ?? new UploadRoutingOptions
            {
                EnableFailover = ReadBool("UploadRouting:EnableFailover", true),
                MaxFailoverAttempts = ReadInt("UploadRouting:MaxFailoverAttempts", 3),
                FailureThreshold = ReadInt("UploadRouting:FailureThreshold", 3)
            };

        // --- Helpers ---

        private async Task<SystemSettings?> LoadAsync()
        {
            // One row by design. Ordering by Id keeps the choice stable if a duplicate ever
            // slips in through a concurrent create.
            var all = await unitOfWork.Repository<SystemSettings>().ListAsync(orderBy: q => q.OrderBy(s => s.Id), take: 1);
            return all.FirstOrDefault();
        }

        private static void Validate(UpdateSystemSettingsRequestDto r)
        {
            if (r.MaxFailoverAttempts is < 0 or > 20)
                throw new ValidationException("InvalidSetting", "Max failover attempts must be between 0 and 20.");

            if (r.FailureThreshold is < 1 or > 100)
                throw new ValidationException("InvalidSetting", "Failure threshold must be between 1 and 100.");

            if (r.HealthCacheTtlSeconds is < 10 or > 3600)
                throw new ValidationException("InvalidSetting", "Health cache TTL must be between 10 and 3600 seconds.");

            if (r.ProbeTimeoutSeconds is < 1 or > 120)
                throw new ValidationException("InvalidSetting", "Probe timeout must be between 1 and 120 seconds.");

            if (r.QuotaHeadroomRatio is < 0 or > 1)
                throw new ValidationException("InvalidSetting", "Quota headroom ratio must be between 0 and 1.");

            if (r.DegradedFreeQuotaRatio is < 0 or > 1)
                throw new ValidationException("InvalidSetting", "Degraded free quota ratio must be between 0 and 1.");

            // A download holds its Hangfire worker for the whole transfer, so this is the
            // ceiling on concurrent transfers. Zero would stall every job silently.
            if (r.HangfireWorkerCount is < 1 or > 100)
                throw new ValidationException("InvalidSetting", "Worker count must be between 1 and 100.");
        }

        private bool ReadBool(string key, bool fallback)
            => bool.TryParse(configuration[key], out var value) ? value : fallback;

        private int ReadInt(string key, int fallback)
            => int.TryParse(configuration[key], out var value) ? value : fallback;

        private static UploadRoutingOptions ToRoutingOptions(SystemSettings s) => new()
        {
            EnableFailover = s.EnableFailover,
            MaxFailoverAttempts = s.MaxFailoverAttempts,
            FailureThreshold = s.FailureThreshold,
            HealthCacheTtl = TimeSpan.FromSeconds(s.HealthCacheTtlSeconds),
            QuotaHeadroomRatio = s.QuotaHeadroomRatio,
            DegradedFreeQuotaRatio = s.DegradedFreeQuotaRatio,
            ProbeTimeout = TimeSpan.FromSeconds(s.ProbeTimeoutSeconds)
        };

        private static SystemSettingsDto ToDto(SystemSettings s) => new()
        {
            EnableFailover = s.EnableFailover,
            MaxFailoverAttempts = s.MaxFailoverAttempts,
            FailureThreshold = s.FailureThreshold,
            HealthCacheTtlSeconds = s.HealthCacheTtlSeconds,
            QuotaHeadroomRatio = s.QuotaHeadroomRatio,
            DegradedFreeQuotaRatio = s.DegradedFreeQuotaRatio,
            ProbeTimeoutSeconds = s.ProbeTimeoutSeconds,
            HangfireWorkerCount = s.HangfireWorkerCount,
            EnablePrometheus = s.EnablePrometheus,
            EnableTracing = s.EnableTracing
        };
    }
}
