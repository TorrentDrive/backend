
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Entities.Torrents;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Core.Entities.Jobs
{
    public class UserJob : BaseEntity, IRecoverableJob
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;


        /// <summary>
        /// The storage profile this job currently uploads to. Automatic failover and
        /// user routing both rewrite this field; <see cref="OriginalStorageProfileId"/>
        /// keeps the profile the job was created with.
        /// </summary>
        public int StorageProfileId { get; set; }
        public UserStorageProfile StorageProfile { get; set; } = null!;

        /// <summary>
        /// Profile the job was created with. Set the first time the job is rerouted, so the
        /// original destination stays visible after a failover.
        /// </summary>
        public int? OriginalStorageProfileId { get; set; }

        /// <summary>
        /// When false, an unhealthy destination fails the job instead of moving it to
        /// another profile. Set false when a user pins the job to a specific drive.
        /// </summary>
        public bool AllowStorageFailover { get; set; } = true;

        /// <summary>
        /// Number of automatic reroutes performed for this job. Bounded by
        /// <c>UploadRoutingOptions.MaxFailoverAttempts</c> so a job cannot walk every
        /// profile a user owns.
        /// </summary>
        public int FailoverAttempts { get; set; }

        /// <summary>Why the job last changed storage profile.</summary>
        public StorageRouteReason LastRouteReason { get; set; } = StorageRouteReason.None;

        public JobStatus Status { get; set; } = JobStatus.QUEUED;

        public JobType Type { get; set; } = JobType.Torrent;

        public int RequestFileId { get; set; }

        public RequestedFile RequestFile { get; set; } = null!;

        public string? ErrorMessage { get; set; }
        public string? CurrentState { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        public DateTime? NextRetryAt { get; set; }
      
        public DateTime? LastHeartbeat { get; set; }

        public string? HangfireJobId { get; set; }
        public string? HangfireUploadJobId { get; set; }

        public string? DownloadPath { get; set; }

      
        public long BytesDownloaded { get; set; }

        public long BytesUploaded { get; set; }

        public long TotalBytes { get; set; }

        /// <summary>
        /// Current download rate in bytes per second, refreshed by the torrent worker on
        /// each progress write. Zero when nothing is transferring; stale once the job
        /// leaves the download phase, so read it alongside Status.
        /// </summary>
        public double DownloadSpeedBytesPerSecond { get; set; }

        /// <summary>
        /// Current upload rate in bytes per second, refreshed by the upload worker on
        /// each progress write.
        /// </summary>
        public double UploadSpeedBytesPerSecond { get; set; }



        public string[]? SelectedFilePaths { get; set; }



        /// <summary>
        /// Status change history for this job, providing a complete audit trail.
        /// </summary>
        public ICollection<JobStatusHistory> StatusHistory { get; set; } = new List<JobStatusHistory>();
    }
}