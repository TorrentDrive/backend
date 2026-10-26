using TorrentDrive.Core.Enums;
using System.Collections.Generic;

namespace TorrentDrive.Core.DTOs.Jobs
{
    public class JobStatisticsDto
    {
        public int TotalJobs { get; set; }
        public int ActiveJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public int QueuedJobs { get; set; }
        public int DownloadingJobs { get; set; }
        public int PendingUploadJobs { get; set; }
        public int UploadingJobs { get; set; }
        public int RetryingJobs { get; set; }
        public int CancelledJobs { get; set; }

        public int PinnedJobs { get; set; }
        public int ReroutedJobs { get; set; }
        public int StalledJobs { get; set; }
        public int SelectiveJobs { get; set; }

        public long TotalBytesTransferred { get; set; }
        public double TotalDownloadSpeedBytesPerSecond { get; set; }
        public double TotalUploadSpeedBytesPerSecond { get; set; }

        /// <summary>
        /// Per-status counts for this user. Only statuses with Count > 0 are returned by the service.
        /// </summary>
        public List<JobStatusFilterDto> StatusFilters { get; set; } = new();

        public Dictionary<string, int> ProviderCounts { get; set; } = new();
        public Dictionary<string, int> CategoryCounts { get; set; } = new();
        public Dictionary<string, int> SizeBucketCounts { get; set; } = new();
        public List<StorageProfileFacetDto> ProfileCounts { get; set; } = new();
    }

    public class JobStatusFilterDto
    {
        public JobStatus Status { get; set; }
        public int Count { get; set; }
    }

    public class StorageProfileFacetDto
    {
        public int StorageProfileId { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
