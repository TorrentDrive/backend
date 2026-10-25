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
    /// Picks the storage profile an upload targets. When the job's current destination
    /// cannot take the upload — revoked token, exhausted quota, repeated failures — the
    /// job is moved to another healthy profile of the same provider instead of failing.
    /// </summary>
    /// <remarks>
    /// Rerouting only changes the job's destination. Re-dispatching the upload after a
    /// user-initiated route change is the caller's job (see <c>JobService</c>), because
    /// enqueueing belongs to the layer that owns the Hangfire client.
    /// </remarks>
    public class UploadRoutingService(
        IUnitOfWork unitOfWork,
        IStorageProfileHealthService healthService,
        IRedisCacheService redisCache,
        IJobStatusService jobStatusService,
        // Snapshot, not IOptions: these values now come from the Settings tab, and a
        // snapshot is rebuilt per scope so a change is picked up without a restart.
        IOptionsSnapshot<UploadRoutingOptions> options,
        ILogger<UploadRoutingService> logger) : IUploadRoutingService
    {
        private readonly UploadRoutingOptions _options = options.Value;
        private static readonly TimeSpan AttemptHistoryTtl = TimeSpan.FromDays(7);

        public async Task<StorageRouteResult> ResolveTargetAsync(
            UserJob job,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default)
        {
            var current = await LoadProfileAsync(job.StorageProfileId);

            if (current is { IsActive: true } &&
                await healthService.IsUsableAsync(current, requiredBytes, cancellationToken))
            {
                await RecordAttemptAsync(job.Id, current.Id);
                return StorageRouteResult.Unchanged(current);
            }

            var reason = DetermineReason(current);
            var detail = await DescribeAsync(current, cancellationToken);

            if (!CanFailover(job))
            {
                logger.LogWarning(
                    "Upload destination unusable and failover disabled | JobId: {JobId} | ProfileId: {ProfileId} | Reason: {Reason}",
                    job.Id, job.StorageProfileId, reason);

                return StorageRouteResult.NoTarget(
                    $"{ProfileLabel(current)} cannot accept this upload ({detail}), and this job is pinned to it.",
                    reason);
            }

            return await MoveToHealthyProfileAsync(job, current, reason, detail, requiredBytes, cancellationToken);
        }

        public async Task<StorageRouteResult> FailoverAsync(
            UserJob job,
            Exception? cause,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default)
        {
            var current = await LoadProfileAsync(job.StorageProfileId);

            if (current != null && cause != null)
            {
                await healthService.RecordFailureAsync(current, cause, cancellationToken);
            }

            if (!CanFailover(job))
            {
                return StorageRouteResult.NoTarget(
                    $"{ProfileLabel(current)} failed and this job is pinned to it.",
                    DetermineReason(current));
            }

            var detail = cause?.Message ?? await DescribeAsync(current, cancellationToken);

            return await MoveToHealthyProfileAsync(
                job, current, DetermineReason(current), detail, requiredBytes, cancellationToken);
        }

        public async Task<StorageRouteResult> RouteToProfileAsync(
            UserJob job,
            int targetProfileId,
            int userId,
            bool allowFailover,
            CancellationToken cancellationToken = default)
        {
            var target = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.Id == targetProfileId && p.UserId == userId && p.IsActive)
                ?? throw new NotFoundException("ProfileNotFound", "Storage profile not found.");

            var health = await healthService.GetHealthAsync(target, forceRefresh: true, cancellationToken);
            if (!health.IsUsable)
            {
                throw new BusinessRuleException(
                    "StorageUnhealthy",
                    health.Message ?? $"{target.ProfileName} is not accepting uploads right now.");
            }

            var previous = await LoadProfileAsync(job.StorageProfileId);
            var changed = job.StorageProfileId != target.Id;

            if (changed)
            {
                ApplyRoute(job, target, StorageRouteReason.UserRouted);
            }

            job.AllowStorageFailover = allowFailover;
            await unitOfWork.Complete();

            // A user-chosen destination is a fresh start: forget which profiles failed
            // before so failover can reconsider them.
            await ClearAttemptHistoryAsync(job.Id);
            await RecordAttemptAsync(job.Id, target.Id);

            if (changed)
            {
                logger.LogInformation(
                    "Job routed to a different storage profile | JobId: {JobId} | From: {FromId} | To: {ToId} | UserId: {UserId}",
                    job.Id, previous?.Id, target.Id, userId);

                await RecordTimelineAsync(job, previous, target, StorageRouteReason.UserRouted,
                    $"Destination changed to {target.ProfileName} by user.");
            }

            return new StorageRouteResult
            {
                Target = target,
                Rerouted = changed,
                PreviousProfileId = previous?.Id,
                PreviousProfileName = previous?.ProfileName,
                Reason = StorageRouteReason.UserRouted,
                Message = changed
                    ? $"Upload will go to {target.ProfileName}."
                    : $"Upload already targets {target.ProfileName}."
            };
        }

        public async Task<IReadOnlyList<int>> GetAttemptedProfileIdsAsync(int jobId)
        {
            var raw = await redisCache.GetAsync(AttemptKey(jobId));
            if (string.IsNullOrEmpty(raw)) return [];

            return [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out var id) ? id : 0)
                .Where(id => id > 0)];
        }

        public Task ClearAttemptHistoryAsync(int jobId) => redisCache.DeleteAsync(AttemptKey(jobId));

        // --- Core failover ---

        private async Task<StorageRouteResult> MoveToHealthyProfileAsync(
            UserJob job,
            UserStorageProfile? current,
            StorageRouteReason reason,
            string? detail,
            long requiredBytes,
            CancellationToken cancellationToken)
        {
            var attempted = new HashSet<int>(await GetAttemptedProfileIdsAsync(job.Id));
            attempted.Add(job.StorageProfileId);

            var providerType = current?.ProviderType ?? StorageProviderType.GoogleDrive;

            var candidates = await LoadCandidatesAsync(job.UserId, providerType, attempted);

            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!await healthService.IsUsableAsync(candidate, requiredBytes, cancellationToken))
                {
                    logger.LogDebug("Skipping unhealthy failover candidate | JobId: {JobId} | ProfileId: {ProfileId}",
                        job.Id, candidate.Id);
                    continue;
                }

                ApplyRoute(job, candidate, reason);
                job.FailoverAttempts++;
                await unitOfWork.Complete();

                await RecordAttemptAsync(job.Id, candidate.Id);

                logger.LogWarning(
                    "Upload rerouted to a healthy storage profile | JobId: {JobId} | From: {FromId} ({FromName}) | To: {ToId} ({ToName}) | Reason: {Reason}",
                    job.Id, current?.Id, current?.ProfileName, candidate.Id, candidate.ProfileName, reason);

                await RecordTimelineAsync(job, current, candidate, reason,
                    $"{ProfileLabel(current)} is unavailable ({detail}). Upload moved to {candidate.ProfileName}.");

                return new StorageRouteResult
                {
                    Target = candidate,
                    Rerouted = true,
                    PreviousProfileId = current?.Id,
                    PreviousProfileName = current?.ProfileName,
                    Reason = reason,
                    Message = $"Upload moved from {ProfileLabel(current)} to {candidate.ProfileName}."
                };
            }

            logger.LogError(
                "No healthy storage profile available for job | JobId: {JobId} | UserId: {UserId} | Provider: {Provider}",
                job.Id, job.UserId, providerType);

            return StorageRouteResult.NoTarget(
                $"{ProfileLabel(current)} cannot accept this upload ({detail}), and no other healthy {providerType} account is connected.",
                reason);
        }

        private async Task<List<UserStorageProfile>> LoadCandidatesAsync(
            int userId,
            StorageProviderType providerType,
            HashSet<int> excluded)
        {
            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(p =>
                p.UserId == userId &&
                p.IsActive &&
                p.ProviderType == providerType &&
                !p.NeedsReauth);

            return [.. profiles
                .Where(p => !excluded.Contains(p.Id))
                .OrderBy(p => HealthRank(p.HealthStatus))
                .ThenBy(p => p.ConsecutiveFailures)
                .ThenByDescending(p => p.QuotaFreeBytes ?? long.MaxValue)
                .ThenBy(p => p.IsDefault ? 0 : 1)
                .ThenBy(p => p.CreatedAt)];
        }

        private void ApplyRoute(UserJob job, UserStorageProfile target, StorageRouteReason reason)
        {
            job.OriginalStorageProfileId ??= job.StorageProfileId;
            job.StorageProfileId = target.Id;
            job.StorageProfile = target;
            job.LastRouteReason = reason;
        }

        private bool CanFailover(UserJob job)
        {
            if (!_options.EnableFailover) return false;
            if (!job.AllowStorageFailover) return false;

            if (job.FailoverAttempts >= _options.MaxFailoverAttempts)
            {
                logger.LogWarning("Failover budget exhausted | JobId: {JobId} | Attempts: {Attempts}",
                    job.Id, job.FailoverAttempts);
                return false;
            }

            return true;
        }

        private async Task RecordTimelineAsync(
            UserJob job,
            UserStorageProfile? from,
            UserStorageProfile to,
            StorageRouteReason reason,
            string message)
        {
            try
            {
                await jobStatusService.RecordJobEventAsync(
                    job,
                    message,
                    StatusChangeSource.System,
                    new
                    {
                        storageRerouted = true,
                        reason = reason.ToString(),
                        fromProfileId = from?.Id,
                        fromProfileName = from?.ProfileName,
                        toProfileId = to.Id,
                        toProfileName = to.ProfileName,
                        failoverAttempts = job.FailoverAttempts
                    });
            }
            catch (Exception ex)
            {
                // The reroute itself already succeeded; a missing timeline entry must not undo it.
                logger.LogWarning(ex, "Could not record reroute on the job timeline | JobId: {JobId}", job.Id);
            }
        }

        private async Task<UserStorageProfile?> LoadProfileAsync(int profileId) =>
            await unitOfWork.Repository<UserStorageProfile>().GetByIdAsync(profileId);

        private static StorageRouteReason DetermineReason(UserStorageProfile? profile)
        {
            if (profile == null || !profile.IsActive) return StorageRouteReason.FailoverInactive;
            if (profile.NeedsReauth) return StorageRouteReason.FailoverNeedsReauth;
            if (profile.QuotaFreeBytes == 0) return StorageRouteReason.FailoverQuotaExceeded;
            return StorageRouteReason.FailoverUnhealthy;
        }

        private async Task<string> DescribeAsync(UserStorageProfile? profile, CancellationToken cancellationToken)
        {
            if (profile == null) return "profile no longer exists";
            if (!profile.IsActive) return "profile disconnected";

            var health = await healthService.GetHealthAsync(profile, forceRefresh: false, cancellationToken);
            return health.Message ?? health.Reason ?? health.Status.ToString();
        }

        private static string ProfileLabel(UserStorageProfile? profile) =>
            profile?.ProfileName is { Length: > 0 } name ? name : "The previous destination";

        private static int HealthRank(StorageHealthStatus status) => status switch
        {
            StorageHealthStatus.Healthy => 0,
            StorageHealthStatus.Unknown => 1,
            StorageHealthStatus.Degraded => 2,
            _ => 3
        };

        private async Task RecordAttemptAsync(int jobId, int profileId)
        {
            try
            {
                var attempted = await GetAttemptedProfileIdsAsync(jobId);
                if (attempted.Contains(profileId)) return;

                var updated = string.Join(',', attempted.Append(profileId));
                await redisCache.SetAsync(AttemptKey(jobId), updated, AttemptHistoryTtl);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not record routing attempt | JobId: {JobId} | ProfileId: {ProfileId}", jobId, profileId);
            }
        }

        private static string AttemptKey(int jobId) => $"upload:route:attempted:{jobId}";
    }
}
