using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Options;

namespace TorrentDrive.Infrastructure.Services.Health
{
    /// <summary>
    /// Caches provider health probes in Redis and keeps the persisted health columns on
    /// <see cref="UserStorageProfile"/> in sync, so both the API and the upload workers
    /// see the same view of which drives can take an upload.
    /// </summary>
    public class StorageProfileHealthService(
        IUnitOfWork unitOfWork,
        IEnumerable<IStorageHealthProbe> probes,
        IRedisCacheService redisCache,
        // Snapshot, not IOptions: these values now come from the Settings tab, and a
        // snapshot is rebuilt per scope so a change is picked up without a restart.
        IOptionsSnapshot<UploadRoutingOptions> options,
        ILogger<StorageProfileHealthService> logger) : IStorageProfileHealthService
    {
        private readonly UploadRoutingOptions _options = options.Value;

        private static readonly JsonSerializerOptions CacheJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task<StorageProfileHealthDto> GetHealthAsync(
            UserStorageProfile profile,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (!profile.IsActive)
            {
                return BuildDto(profile, StorageHealthStatus.Unhealthy, "Disconnected",
                    "This storage profile is disconnected.", fromCache: false);
            }

            if (!forceRefresh)
            {
                var cached = await ReadCacheAsync(profile.Id);
                if (cached != null)
                {
                    cached.FromCache = true;
                    return cached;
                }
            }

            var probe = probes.FirstOrDefault(p => p.ProviderType == profile.ProviderType);
            if (probe == null)
            {
                logger.LogWarning("No health probe registered for provider {Provider}; treating profile {ProfileId} as healthy",
                    profile.ProviderType, profile.Id);

                return BuildDto(profile, StorageHealthStatus.Unknown, null, null, fromCache: false);
            }

            StorageProbeResult result;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProbeTimeout);

            try
            {
                result = await probe.ProbeAsync(profile, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = StorageProbeResult.Unhealthy("Timeout",
                    $"{profile.ProviderType} did not respond within {_options.ProbeTimeout.TotalSeconds:F0}s.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health probe threw for profile {ProfileId}", profile.Id);
                result = StorageProbeResult.Unhealthy("ProbeFailed", ex.Message);
            }

            result = ApplyQuotaRules(result);

            await PersistAsync(profile, result, cancellationToken);

            var dto = BuildDto(profile, result.Status, result.Reason, result.Message, fromCache: false);
            await WriteCacheAsync(dto);
            return dto;
        }

        public async Task<StorageProfileHealthDto> GetHealthAsync(
            int userId,
            int profileId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var profile = await LoadProfileAsync(userId, profileId)
                ?? throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            return await GetHealthAsync(profile, forceRefresh, cancellationToken);
        }

        public async Task<List<StorageProfileHealthDto>> GetHealthForUserAsync(
            int userId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(p => p.UserId == userId && p.IsActive);

            var results = new List<StorageProfileHealthDto>(profiles.Count);

            // Sequential on purpose: probes share the scoped DbContext through
            // GetAccessTokenAsync, which persists refreshed tokens.
            foreach (var profile in profiles.OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.CreatedAt))
            {
                results.Add(await GetHealthAsync(profile, forceRefresh, cancellationToken));
            }

            return results;
        }

        public async Task<bool> IsUsableAsync(
            UserStorageProfile profile,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default)
        {
            var health = await GetHealthAsync(profile, forceRefresh: false, cancellationToken);

            if (!health.IsUsable) return false;

            return HasRoomFor(health.QuotaFreeBytes, requiredBytes);
        }

        public async Task RecordFailureAsync(
            UserStorageProfile profile,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            var probe = probes.FirstOrDefault(p => p.ProviderType == profile.ProviderType);
            var classified = probe?.ClassifyFailure(exception);

            profile.ConsecutiveFailures++;
            profile.LastHealthCheckAt = DateTime.UtcNow;
            profile.LastHealthError = classified?.Message ?? exception.Message;

            if (classified is { Status: StorageHealthStatus.Unhealthy })
            {
                profile.HealthStatus = StorageHealthStatus.Unhealthy;

                logger.LogWarning("Storage profile {ProfileId} marked Unhealthy | Reason: {Reason}",
                    profile.Id, classified.Reason);
            }
            else if (profile.ConsecutiveFailures >= _options.FailureThreshold)
            {
                profile.HealthStatus = StorageHealthStatus.Unhealthy;

                logger.LogWarning("Storage profile {ProfileId} marked Unhealthy after {Count} consecutive failures",
                    profile.Id, profile.ConsecutiveFailures);
            }
            else
            {
                profile.HealthStatus = StorageHealthStatus.Degraded;
            }

            await unitOfWork.Complete();
            await InvalidateAsync(profile.Id);
        }

        public async Task RecordSuccessAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            if (profile.ConsecutiveFailures == 0 &&
                profile.HealthStatus == StorageHealthStatus.Healthy &&
                profile.LastHealthError == null)
            {
                return;
            }

            profile.ConsecutiveFailures = 0;
            profile.LastHealthError = null;
            profile.HealthStatus = StorageHealthStatus.Healthy;
            profile.LastHealthCheckAt = DateTime.UtcNow;

            await unitOfWork.Complete();
            await InvalidateAsync(profile.Id);
        }

        public Task InvalidateAsync(int profileId) => redisCache.DeleteAsync(CacheKey(profileId));

        // --- Helpers ---

        private async Task<UserStorageProfile?> LoadProfileAsync(int userId, int profileId)
        {
            return await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(p => p.Id == profileId && p.UserId == userId);
        }

        /// <summary>
        /// Turns a raw probe result into a final status: a healthy account with almost no
        /// free space is Degraded, and one with none left is Unhealthy.
        /// </summary>
        private StorageProbeResult ApplyQuotaRules(StorageProbeResult result)
        {
            if (result.Status != StorageHealthStatus.Healthy) return result;
            if (result.QuotaTotalBytes is not > 0 || result.QuotaUsedBytes == null) return result;

            var free = Math.Max(0, result.QuotaTotalBytes.Value - result.QuotaUsedBytes.Value);

            if (free == 0)
            {
                var exhausted = StorageProbeResult.Unhealthy(
                    "QuotaExceeded", "This account has no free storage left.", requiresUserAction: true);
                exhausted.QuotaTotalBytes = result.QuotaTotalBytes;
                exhausted.QuotaUsedBytes = result.QuotaUsedBytes;
                return exhausted;
            }

            var freeRatio = (double)free / result.QuotaTotalBytes.Value;
            if (freeRatio <= _options.DegradedFreeQuotaRatio)
            {
                return StorageProbeResult.Degraded(
                    "LowQuota",
                    $"Only {FormatBytes(free)} of free storage left.",
                    result.QuotaTotalBytes,
                    result.QuotaUsedBytes);
            }

            return result;
        }

        private async Task PersistAsync(UserStorageProfile profile, StorageProbeResult result, CancellationToken cancellationToken)
        {
            profile.HealthStatus = result.Status;
            profile.LastHealthCheckAt = DateTime.UtcNow;
            profile.LastHealthError = result.Status == StorageHealthStatus.Healthy ? null : result.Message;
            profile.QuotaTotalBytes = result.QuotaTotalBytes;
            profile.QuotaUsedBytes = result.QuotaUsedBytes;

            if (result.Status == StorageHealthStatus.Healthy)
                profile.ConsecutiveFailures = 0;

            try
            {
                await unitOfWork.Complete();
            }
            catch (Exception ex)
            {
                // Health state is advisory — never let a write failure break the caller.
                logger.LogWarning(ex, "Could not persist health state for profile {ProfileId}", profile.Id);
            }
        }

        private StorageProfileHealthDto BuildDto(
            UserStorageProfile profile,
            StorageHealthStatus status,
            string? reason,
            string? message,
            bool fromCache)
        {
            return new StorageProfileHealthDto
            {
                ProfileId = profile.Id,
                ProfileName = profile.ProfileName,
                ProviderType = profile.ProviderType.ToString(),
                Email = profile.Email,
                Status = status,
                IsUsable = status is StorageHealthStatus.Healthy or StorageHealthStatus.Degraded or StorageHealthStatus.Unknown,
                Reason = reason,
                Message = message,
                NeedsReauth = profile.NeedsReauth,
                ConsecutiveFailures = profile.ConsecutiveFailures,
                QuotaTotalBytes = profile.QuotaTotalBytes,
                QuotaUsedBytes = profile.QuotaUsedBytes,
                QuotaFreeBytes = profile.QuotaFreeBytes,
                CheckedAt = profile.LastHealthCheckAt,
                FromCache = fromCache
            };
        }

        /// <summary>
        /// A null free-quota means the provider reports no limit, which always has room.
        /// </summary>
        private bool HasRoomFor(long? freeBytes, long requiredBytes)
        {
            if (freeBytes == null || requiredBytes <= 0) return true;

            var needed = (long)(requiredBytes * (1 + _options.QuotaHeadroomRatio));
            return freeBytes.Value >= needed;
        }

        private async Task<StorageProfileHealthDto?> ReadCacheAsync(int profileId)
        {
            try
            {
                var json = await redisCache.GetAsync(CacheKey(profileId));
                return json == null ? null : JsonSerializer.Deserialize<StorageProfileHealthDto>(json, CacheJsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read cached health for profile {ProfileId}", profileId);
                return null;
            }
        }

        private async Task WriteCacheAsync(StorageProfileHealthDto dto)
        {
            try
            {
                await redisCache.SetAsync(
                    CacheKey(dto.ProfileId),
                    JsonSerializer.Serialize(dto, CacheJsonOptions),
                    _options.HealthCacheTtl);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not cache health for profile {ProfileId}", dto.ProfileId);
            }
        }

        private static string CacheKey(int profileId) => $"storage:health:{profileId}";

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }
}
