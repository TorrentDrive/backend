using TorrentDrive.Core.Entities.Jobs;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Reclaims a job's local download directory once its contents have been
    /// uploaded to the user's storage provider.
    /// </summary>
    public interface IDownloadCleanupService
    {
        /// <summary>
        /// Deletes the job's download directory. Only call this after the job has
        /// transitioned to COMPLETED: a failed or retrying job still needs its files.
        /// Never throws — a cleanup failure must not fail an upload that succeeded.
        /// </summary>
        /// <returns>Bytes reclaimed, or 0 if nothing was deleted.</returns>
        Task<long> CleanupAfterUploadAsync(UserJob job, CancellationToken cancellationToken = default);
    }
}
