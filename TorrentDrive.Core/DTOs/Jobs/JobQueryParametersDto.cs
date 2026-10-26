using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.DTOs.Jobs
{
    public class JobQueryParametersDto
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Backward-compatible single status filter.
        /// </summary>
        public JobStatus? Status { get; set; }

        /// <summary>
        /// Optional comma-separated list of statuses (enum names or integer values).
        /// </summary>
        public string? Statuses { get; set; }

        /// <summary>
        /// High-level lifecycle phase filter:
        /// all, active, downloading, uploading, queued, retrying, completed, failed, cancelled.
        /// </summary>
        public string? Phase { get; set; }

        /// <summary>
        /// Free-text and token-aware search query (supports provider:, phase:, status:, is:, type:, category:, ext:, size:, id:, #id).
        /// </summary>
        public string? Search { get; set; }

        /// <summary>
        /// Alias query parameter for Search (?q=...).
        /// </summary>
        public string? Q { get; set; }

        /// <summary>
        /// Storage provider type filter (GoogleDrive, gdrive, S3, s3).
        /// </summary>
        public string? Provider { get; set; }

        /// <summary>
        /// Specific storage profile ID filter.
        /// </summary>
        public int? StorageProfileId { get; set; }

        /// <summary>
        /// Media category filter: video, audio, archive, document, software, other.
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// File size bucket filter: small (&lt;1gb), medium (1-10gb), large (10-50gb), xl (&gt;50gb).
        /// </summary>
        public string? SizeBucket { get; set; }

        /// <summary>
        /// Creation date range filter: today / 24h, 7d, 30d, 90d.
        /// </summary>
        public string? DateRange { get; set; }

        /// <summary>
        /// Filter by pinned destination (!AllowStorageFailover).
        /// </summary>
        public bool? IsPinned { get; set; }

        /// <summary>
        /// Filter by whether the job was rerouted from its original storage profile.
        /// </summary>
        public bool? WasRerouted { get; set; }

        /// <summary>
        /// Filter by whether the job is currently stalled.
        /// </summary>
        public bool? IsStalled { get; set; }

        /// <summary>
        /// Filter by whether the user selected a subset of torrent files.
        /// </summary>
        public bool? IsSelective { get; set; }

        /// <summary>
        /// Filter by whether the job currently has a non-empty ErrorMessage.
        /// </summary>
        public bool? HasError { get; set; }

        /// <summary>
        /// Field to sort by: createdAt, size, progress, speed, name, completedAt, updatedAt.
        /// </summary>
        public string? SortBy { get; set; } = "createdAt";

        /// <summary>
        /// Sort direction: asc or desc.
        /// </summary>
        public string? SortDir { get; set; } = "desc";

        public string? EffectiveSearch => !string.IsNullOrWhiteSpace(Search) ? Search : Q;
    }
}
