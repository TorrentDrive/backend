using TorrentDrive.Core.DTOs.Maintenance;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Inspects and reclaims the shared downloads volume. Scoped to one user: a
    /// directory belonging to another user's job is never counted or deleted.
    /// </summary>
    public interface IDownloadMaintenanceService
    {
        /// <summary>
        /// Counts what is on disk, split into what Purge would delete, what it keeps,
        /// and directories with no matching job.
        /// </summary>
        Task<DownloadStoragePreviewDto> GetPreviewAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the download directories of the user's COMPLETED and CANCELLED jobs.
        /// Nothing else is touched.
        /// </summary>
        Task<PurgeDownloadsResultDto> PurgeAsync(int userId, CancellationToken cancellationToken = default);
    }
}
