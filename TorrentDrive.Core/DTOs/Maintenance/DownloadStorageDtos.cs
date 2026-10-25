namespace TorrentDrive.Core.DTOs.Maintenance
{
    /// <summary>
    /// One download directory found on the downloads volume.
    /// </summary>
    public record DownloadDirectoryDto
    {
        /// <summary>Job id parsed from the directory name.</summary>
        public int? JobId { get; init; }

        public string DirectoryName { get; init; } = string.Empty;
        public long SizeBytes { get; init; }

        /// <summary>Status of the owning job, or null when no job row matches.</summary>
        public string? JobStatus { get; init; }

        public string? TorrentName { get; init; }
        public DateTime? CompletedAt { get; init; }
    }

    /// <summary>
    /// What the downloads volume currently holds, split by what Purge may remove.
    /// </summary>
    public record DownloadStoragePreviewDto
    {
        /// <summary>Directories for COMPLETED or CANCELLED jobs — what Purge deletes.</summary>
        public List<DownloadDirectoryDto> Purgeable { get; init; } = new();
        public int PurgeableCount { get; init; }
        public long PurgeableBytes { get; init; }

        /// <summary>
        /// Directories for jobs still running, retrying, or failed. Kept: a retrying or
        /// resumable job still needs its files.
        /// </summary>
        public int RetainedCount { get; init; }
        public long RetainedBytes { get; init; }

        /// <summary>
        /// Directories with no matching job row at all. Reported so the space is visible,
        /// never deleted — the app cannot confirm they are dead.
        /// </summary>
        public int OrphanedCount { get; init; }
        public long OrphanedBytes { get; init; }

        public long TotalBytes { get; init; }

        /// <summary>Set when the volume could not be read, e.g. a misconfigured path.</summary>
        public string? Warning { get; init; }
    }

    public record PurgeDownloadsResultDto
    {
        public int DeletedCount { get; init; }
        public long FreedBytes { get; init; }
        public int FailedCount { get; init; }
        public List<string> Failures { get; init; } = new();
    }
}
