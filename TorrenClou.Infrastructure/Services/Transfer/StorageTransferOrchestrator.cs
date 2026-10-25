using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Infrastructure.Services.Transfer
{
    /// <summary>
    /// Deep storage transfer module that encapsulates the entire cloud upload lifecycle:
    /// distributed locking with automatic background renewal, status transitions,
    /// destination health failover, file system traversal, speed metrics, and post-upload cleanup.
    /// </summary>
    public class StorageTransferOrchestrator(
        IEnumerable<IStorageDestination> destinations,
        IRedisLockService redisLockService,
        IJobStatusService jobStatusService,
        IUploadRoutingService uploadRoutingService,
        IStorageProfileHealthService storageHealthService,
        IDownloadCleanupService downloadCleanupService,
        ITransferSpeedMetrics speedMetrics,
        IServiceScopeFactory serviceScopeFactory,
        IJobExecutionTracker executionTracker,
        ILogger<StorageTransferOrchestrator> logger) : IStorageTransferOrchestrator
    {
        private const int HeartbeatIntervalSeconds = 15;

        public async Task ExecuteTransferAsync(
            UserJob job,
            StorageProviderType expectedProvider,
            Func<UserJob, string, bool, Task> onMarkFailed,
            CancellationToken cancellationToken)
        {
            var destination = destinations.FirstOrDefault(d => d.ProviderType == expectedProvider)
                ?? throw new NotSupportedException($"No IStorageDestination adapter registered for provider {expectedProvider}");

            var logPrefix = $"[{expectedProvider.ToString().ToUpperInvariant()}:TRANSFER]";
            var lockKey = $"{destination.LockKeyPrefix}:lock:{job.Id}";
            var lockExpiry = TimeSpan.FromHours(2);

            using var distributedLock = await redisLockService.AcquireLockAsync(lockKey, lockExpiry, cancellationToken);
            if (distributedLock == null)
            {
                logger.LogWarning("{LogPrefix} Job is already being processed by another instance | JobId: {JobId}", logPrefix, job.Id);
                return;
            }

            logger.LogInformation("{LogPrefix} Acquired distributed lock | JobId: {JobId} | Expiry: {Expiry}", logPrefix, job.Id, lockExpiry);

            await TransitionToUploadingAsync(job, expectedProvider, logPrefix);

            if (string.IsNullOrEmpty(job.DownloadPath) || !Directory.Exists(job.DownloadPath))
            {
                await FailWithCleanupAsync(job, destination, onMarkFailed, $"Download path not found: {job.DownloadPath}", hasRetries: false, logPrefix);
                return;
            }

            if (job.StorageProfile == null || job.StorageProfile.ProviderType != expectedProvider)
            {
                await FailWithCleanupAsync(job, destination, onMarkFailed, "Invalid storage profile.", hasRetries: false, logPrefix);
                return;
            }

            var allFiles = GetFilesToUpload(job.DownloadPath, logPrefix);
            if (allFiles.Length == 0)
            {
                await FailWithCleanupAsync(job, destination, onMarkFailed, "No valid files found in download path.", hasRetries: false, logPrefix);
                return;
            }

            var totalBytes = allFiles.Sum(f => f.Length);
            var uploadStartTime = DateTime.UtcNow;

            var route = await uploadRoutingService.ResolveTargetAsync(job, totalBytes, cancellationToken);
            if (!route.HasTarget)
            {
                await FailWithCleanupAsync(
                    job,
                    destination,
                    onMarkFailed,
                    route.Message ?? "No healthy storage destination is available for this upload.",
                    hasRetries: false,
                    logPrefix);
                return;
            }

            if (route.Rerouted)
            {
                logger.LogWarning("{LogPrefix} Destination rerouted | JobId: {JobId} | {Message}", logPrefix, job.Id, route.Message);
                await executionTracker.RecordHeartbeatAsync(job.Id, $"Switched destination to {route.Target!.ProfileName}");
            }

            using var lockLossCts = new CancellationTokenSource();
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lockLossCts.Token);
            var heartbeatTask = RunHeartbeatAndLockRenewalLoopAsync(job.Id, distributedLock, lockLossCts, heartbeatCts.Token, logPrefix);

            try
            {
                var outcome = await destination.TransferFilesAsync(job, allFiles, totalBytes, heartbeatCts.Token);

                if (!outcome.AllFilesUploaded)
                {
                    if (job.StorageProfile != null && outcome.LastError != null)
                    {
                        await storageHealthService.RecordFailureAsync(job.StorageProfile, outcome.LastError, CancellationToken.None);
                    }

                    var errorMsg = outcome.LastError != null
                        ? $"Failed to upload {outcome.FailedFiles} of {outcome.TotalFiles} files: {outcome.LastError.Message}"
                        : $"Failed to upload {outcome.FailedFiles} of {outcome.TotalFiles} files.";

                    await FailWithCleanupAsync(job, destination, onMarkFailed, errorMsg, hasRetries: true, logPrefix);
                    return;
                }

                if (job.StorageProfile != null)
                {
                    await storageHealthService.RecordSuccessAsync(job.StorageProfile, CancellationToken.None);
                }

                var duration = (DateTime.UtcNow - uploadStartTime).TotalSeconds;
                speedMetrics.RecordUploadComplete(job.Id, job.UserId, destination.SpeedMetricTag, totalBytes, duration);

                job.CompletedAt = DateTime.UtcNow;
                job.BytesUploaded = totalBytes;
                job.UploadSpeedBytesPerSecond = 0;
                job.CurrentState = "Upload completed successfully";
                job.NextRetryAt = null;

                await jobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.COMPLETED,
                    StatusChangeSource.Worker,
                    metadata: new { totalBytes, filesCount = allFiles.Length, durationSeconds = duration, completedAt = job.CompletedAt });

                logger.LogInformation("{LogPrefix} Completed successfully | JobId: {JobId}", logPrefix, job.Id);

                await downloadCleanupService.CleanupAfterUploadAsync(job, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{LogPrefix} Fatal error during transfer | JobId: {JobId}", logPrefix, job.Id);
                await destination.CleanupOnFailureAsync(job);
                throw;
            }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { /* ignore */ }
            }
        }

        private async Task TransitionToUploadingAsync(UserJob job, StorageProviderType provider, string logPrefix)
        {
            if (job.Status == JobStatus.PENDING_UPLOAD)
            {
                logger.LogInformation("{LogPrefix} Job ready for upload, transitioning to UPLOADING | JobId: {JobId}", logPrefix, job.Id);
                job.CurrentState = $"Starting {provider} upload...";
                job.StartedAt ??= DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;

                await jobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = provider.ToString(), startedAt = job.StartedAt });
            }
            else if (job.Status == JobStatus.UPLOAD_RETRY)
            {
                logger.LogInformation("{LogPrefix} Retrying job | JobId: {JobId} | Retry: {NextRetry}", logPrefix, job.Id, job.NextRetryAt);
                job.CurrentState = $"Retrying {provider} upload...";
                job.LastHeartbeat = DateTime.UtcNow;

                await jobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = provider.ToString(), retrying = true, previousNextRetry = job.NextRetryAt });
            }
            else if (job.Status == JobStatus.UPLOADING)
            {
                await executionTracker.RecordJobStartedAsync(job);
                logger.LogInformation("{LogPrefix} Resuming job from recovery | JobId: {JobId}", logPrefix, job.Id);
            }
        }

        private async Task FailWithCleanupAsync(
            UserJob job,
            IStorageDestination destination,
            Func<UserJob, string, bool, Task> onMarkFailed,
            string errorMessage,
            bool hasRetries,
            string logPrefix)
        {
            try
            {
                await destination.CleanupOnFailureAsync(job);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{LogPrefix} Destination cleanup on failure threw | JobId: {JobId}", logPrefix, job.Id);
            }

            await onMarkFailed(job, errorMessage, hasRetries);
        }

        private async Task RunHeartbeatAndLockRenewalLoopAsync(
            int jobId,
            IRedisLock distributedLock,
            CancellationTokenSource lockLossCts,
            CancellationToken ct,
            string logPrefix)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), ct);

                    using var scope = serviceScopeFactory.CreateScope();
                    var scopedTracker = scope.ServiceProvider.GetRequiredService<IJobExecutionTracker>();
                    await scopedTracker.RecordHeartbeatAsync(jobId);

                    var refreshed = await distributedLock.RefreshAsync();
                    if (!refreshed || !distributedLock.IsOwned)
                    {
                        logger.LogError("{LogPrefix} Distributed lock lost during transfer! Aborting | JobId: {JobId}", logPrefix, jobId);
                        lockLossCts.Cancel();
                        return;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{LogPrefix} Heartbeat/lock renewal cycle encountered error | JobId: {JobId}", logPrefix, jobId);
                }
            }
        }

        public static FileInfo[] GetFilesToUpload(string downloadPath, string logPrefix = "[TRANSFER]")
        {
            var dir = new DirectoryInfo(downloadPath);
            if (!dir.Exists) return [];

            return dir.GetFiles("*", SearchOption.AllDirectories)
                .Where(f =>
                    !f.Name.Equals("dht_nodes.cache", StringComparison.OrdinalIgnoreCase) &&
                    !f.Name.Equals("fastresume", StringComparison.OrdinalIgnoreCase) &&
                    !f.Name.EndsWith(".fresume", StringComparison.OrdinalIgnoreCase) &&
                    !f.Name.EndsWith(".dht", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
