using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Handles job type-specific operations (Torrent, DirectDownload, etc.)
    /// </summary>
    public interface IJobTypeHandler
    {
        /// <summary>
        /// Gets the job type this handler supports
        /// </summary>
        JobType JobType { get; }

        /// <summary>
        /// Gets the Hangfire job interface type for download operations
        /// </summary>
        Type GetDownloadJobInterfaceType();

        /// <summary>
        /// Enqueues a download job via Hangfire
        /// </summary>
        string EnqueueDownloadJob(int jobId, global::Hangfire.IBackgroundJobClient client);

        /// <summary>
        /// Determines if the given job status is in the upload phase
        /// </summary>
        bool IsUploadPhaseStatus(JobStatus status);

        /// <summary>
        /// Gets all failed status values specific to this job type
        /// </summary>
        IEnumerable<JobStatus> GetFailedStatuses();
    }
}


