using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Extensions;

namespace TorrentDrive.Core.DTOs.Jobs
{
    public class JobDto
    {
        public int Id { get; set; }
        public int StorageProfileId { get; set; }
        public string? StorageProfileName { get; set; }

        /// <summary>Profile the job was created with, when it has since been rerouted.</summary>
        public int? OriginalStorageProfileId { get; set; }
        public string? OriginalStorageProfileName { get; set; }

        /// <summary>False when the user pinned this job to its current destination.</summary>
        public bool AllowStorageFailover { get; set; } = true;

        /// <summary>How many times this job has been moved to another profile automatically.</summary>
        public int FailoverAttempts { get; set; }

        /// <summary>Why the destination last changed (None when it never has).</summary>
        public string LastRouteReason { get; set; } = nameof(StorageRouteReason.None);
        public JobStatus Status { get; set; }
        public string Type { get; set; } = string.Empty;
        public int RequestFileId { get; set; }
        public string? RequestFileName { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CurrentState { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public long BytesDownloaded { get; set; }
        public long BytesUploaded { get; set; }
        public long TotalBytes { get; set; }

        /// <summary>Current download rate in bytes/sec. Meaningful only while downloading.</summary>
        public double DownloadSpeedBytesPerSecond { get; set; }

        /// <summary>Current upload rate in bytes/sec. Meaningful only while uploading.</summary>
        public double UploadSpeedBytesPerSecond { get; set; }
        public string[]? SelectedFilePaths { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        
        // Computed properties
        public double ProgressPercentage => TotalBytes > 0 ? (BytesDownloaded / (double)TotalBytes) * 100 : 0;
        public double UploadProgressPercentage => TotalBytes > 0 ? (BytesUploaded / (double)TotalBytes) * 100 : 0;
        public bool IsActive => Status == JobStatus.QUEUED || 
                               Status == JobStatus.DOWNLOADING || 
                               Status == JobStatus.PENDING_UPLOAD || 
                               Status == JobStatus.UPLOADING || 
                               Status == JobStatus.TORRENT_DOWNLOAD_RETRY || 
                               Status == JobStatus.UPLOAD_RETRY;
        public bool CanRetry => Status.IsFailed() && Status != JobStatus.CANCELLED;
        public bool CanCancel => Status.IsCancellable();

        /// <summary>
        /// True while the job is waiting on a queue hand-off (QUEUED or PENDING_UPLOAD)
        /// and can therefore be re-dispatched directly.
        /// </summary>
        public bool CanForceStart =>
            Status == JobStatus.QUEUED ||
            Status == JobStatus.PENDING_UPLOAD;

        /// <summary>True when the destination can still be changed without a retry.</summary>
        public bool CanChangeStorageProfile =>
            Status != JobStatus.COMPLETED &&
            Status != JobStatus.CANCELLED &&
            Status != JobStatus.UPLOADING;

        /// <summary>True when this job is not running on the profile it was created with.</summary>
        public bool WasRerouted => OriginalStorageProfileId.HasValue && OriginalStorageProfileId != StorageProfileId;

        /// <summary>
        /// Status change timeline for this job.
        /// </summary>
        public List<JobTimelineEntryDto> Timeline { get; set; } = [];
    }
}
