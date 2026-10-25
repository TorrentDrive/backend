using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Handles job cancellation logic specific to each job type.
    /// Responsible for stopping active operations and cleaning up resources.
    /// </summary>
    public interface IJobCancellationHandler
    {
        /// <summary>
        /// Gets the job type this handler supports
        /// </summary>
        JobType JobType { get; }

        /// <summary>
        /// Cancels the job by stopping active operations and cleaning up resources.
        /// For torrent jobs: stops the torrent manager, saves state, cleans up temp files.
        /// For other job types: appropriate cleanup logic.
        /// </summary>
        Task CancelJobAsync(UserJob job, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans up resources associated with the job (temp files, locks, etc.)
        /// Called after job cancellation or failure.
        /// </summary>
        Task CleanupResourcesAsync(UserJob job, CancellationToken cancellationToken = default);
    }
}


