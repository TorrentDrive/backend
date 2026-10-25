using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;

namespace TorrenClou.Core.Interfaces
{
    public record DestinationTransferOutcome(int TotalFiles, int FailedFiles, Exception? LastError)
    {
        public bool AllFilesUploaded => FailedFiles == 0;
    }

    /// <summary>
    /// Adapter interface for cloud storage destinations. Sits at the seam beneath
    /// the deep StorageTransferOrchestrator module.
    /// </summary>
    public interface IStorageDestination
    {
        StorageProviderType ProviderType { get; }
        string LockKeyPrefix { get; }
        string SpeedMetricTag { get; }

        Task<DestinationTransferOutcome> TransferFilesAsync(
            UserJob job,
            FileInfo[] files,
            long totalBytes,
            CancellationToken cancellationToken);

        Task CleanupOnFailureAsync(UserJob job);
    }

    /// <summary>
    /// Deep storage transfer module that encapsulates distributed locking, status transitions,
    /// destination failover resolution, file discovery, heartbeat/lock renewal, metrics, and post-upload cleanup.
    /// </summary>
    public interface IStorageTransferOrchestrator
    {
        Task ExecuteTransferAsync(
            UserJob job,
            StorageProviderType expectedProvider,
            Func<UserJob, string, bool, Task> onMarkFailed,
            CancellationToken cancellationToken);
    }
}
