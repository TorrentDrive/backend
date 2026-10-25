using Microsoft.Extensions.Logging;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Scoped context for tracking upload progress across GoogleDriveService and GoogleDriveUploadJob.
    /// Handles progress throttling (5% DB updates, 30s logging) and Redis-backed resume URI caching.
    /// </summary>
    public interface IUploadProgressContext
    {
        bool IsConfigured { get; }

        Task ClearResumeUriAsync(string relativePath);
        /// <summary>
        /// Binds this context to one job and one storage profile. Redis progress keys are
        /// namespaced by <paramref name="storageProfileId"/>, so an upload rerouted to a
        /// different drive starts clean instead of resuming against file IDs that only
        /// exist on the old one.
        /// </summary>
        void Configure(int jobId, int storageProfileId, long totalBytes, ILogger logger, Func<string, double, double, Task> onDbUpdate);
        Task<string?> GetCompletedFileAsync(string relativePath);
        Task<string?> GetResumeUriAsync(string relativePath);
        Task<string?> GetRootFolderIdAsync(int jobId);
        Task MarkBytesCompletedAsync(string fileName, long bytesUploaded);
        Task MarkFileCompletedAsync(string fileName, long fileSize);
        Task ReportProgressAsync(string fileName, long bytesUploaded, long fileSize);
        Task SetCompletedFileAsync(string relativePath, string driveFileId);
        Task SetResumeUriAsync(string relativePath, string resumeUri);
        Task SetRootFolderIdAsync(int jobId, string folderId);

    }
}
