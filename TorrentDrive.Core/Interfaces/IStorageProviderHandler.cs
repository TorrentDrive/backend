using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Handles storage provider-specific operations (Google Drive, OneDrive, etc.)
    /// </summary>
    public interface IStorageProviderHandler
    {
        /// <summary>
        /// Gets the storage provider type this handler supports
        /// </summary>
        StorageProviderType ProviderType { get; }

        /// <summary>
        /// Deletes any provider-specific upload locks for a job
        /// </summary>
        Task<bool> DeleteUploadLockAsync(int jobId);

        /// <summary>
        /// Gets the Hangfire job interface type for upload operations
        /// </summary>
        Type GetUploadJobInterfaceType();

        /// <summary>
        /// Enqueues an upload job via Hangfire
        /// </summary>
        string EnqueueUploadJob(int jobId, global::Hangfire.IBackgroundJobClient client);
    }
}


