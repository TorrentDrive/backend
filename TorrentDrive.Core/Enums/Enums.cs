namespace TorrentDrive.Core.Enums
{
    public enum UserRole { User }

    public enum StorageProviderType { GoogleDrive, S3 }

    /// <summary>
    /// Connection health of a <c>UserStorageProfile</c>, used to decide whether an
    /// upload can be routed to it.
    /// </summary>
    public enum StorageHealthStatus
    {
        /// <summary>Never probed, or the last probe result has expired.</summary>
        Unknown,
        /// <summary>Provider reachable and credentials valid.</summary>
        Healthy,
        /// <summary>Usable but at risk (low free quota, transient failures).</summary>
        Degraded,
        /// <summary>Unusable: revoked token, quota exhausted, or provider errors.</summary>
        Unhealthy
    }

    /// <summary>
    /// Why an upload was moved from one storage profile to another.
    /// </summary>
    public enum StorageRouteReason
    {
        /// <summary>Job kept the profile it was created with.</summary>
        None,
        /// <summary>A user explicitly pinned the job to a storage profile.</summary>
        UserRouted,
        /// <summary>Source profile needs re-authentication.</summary>
        FailoverNeedsReauth,
        /// <summary>Source profile is out of storage quota.</summary>
        FailoverQuotaExceeded,
        /// <summary>Source profile failed its health probe.</summary>
        FailoverUnhealthy,
        /// <summary>Source profile was disconnected or deleted.</summary>
        FailoverInactive
    }

    public enum FileStatus { PENDING, DOWNLOADING, READY, CORRUPTED, DELETED }

    public enum S3UploadProgressStatus
    {
        InProgress,
        Completed,
        Failed
    }
    public enum JobStatus
    {
        QUEUED,
        DOWNLOADING,
        PENDING_UPLOAD,
        UPLOADING,
        TORRENT_DOWNLOAD_RETRY,
        UPLOAD_RETRY,
        COMPLETED,
        FAILED,
        CANCELLED,
        TORRENT_FAILED,
        UPLOAD_FAILED,
        GOOGLE_DRIVE_FAILED
    }

    public enum JobType { Torrent }



    /// <summary>
    /// Identifies the source that triggered a job/sync status change.
    /// </summary>
    public enum StatusChangeSource
    {
        /// <summary>Worker process changed the status during job execution.</summary>
        Worker,
        /// <summary>User action triggered the status change (e.g., cancellation).</summary>
        User,
        /// <summary>System/API triggered the status change (e.g., job creation).</summary>
        System,
        /// <summary>Recovery process changed the status (e.g., recovering stuck jobs).</summary>
        Recovery
    }
}