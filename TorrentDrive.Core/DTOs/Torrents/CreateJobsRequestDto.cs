namespace TorrentDrive.Core.DTOs.Torrents
{
    /// <summary>
    /// Starts several analysed torrents in one call. Each item keeps its own file
    /// selection and may override the batch destination.
    /// </summary>
    public record CreateJobsRequestDto
    {
        /// <summary>Destination used by items that do not set their own.</summary>
        public int? StorageProfileId { get; init; }

        public List<CreateJobItemDto> Items { get; init; } = new();
    }

    public record CreateJobItemDto
    {
        public int TorrentFileId { get; init; }

        /// <summary>Paths to download. Null means every file in the torrent.</summary>
        public string[]? SelectedFilePaths { get; init; }

        /// <summary>Overrides the batch destination for this torrent only.</summary>
        public int? StorageProfileId { get; init; }
    }

    /// <summary>
    /// Per-torrent outcome. A batch reports every item, successful or not, so one bad
    /// torrent never hides the rest.
    /// </summary>
    public record JobCreationOutcomeDto
    {
        public int TorrentFileId { get; init; }
        public int? JobId { get; init; }
        public bool Success { get; init; }

        /// <summary>Domain error code (see DomainException.Code). Null on success.</summary>
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public record BatchJobCreationResultDto
    {
        public List<JobCreationOutcomeDto> Results { get; init; } = new();
        public int SucceededCount { get; init; }
        public int FailedCount { get; init; }
    }
}
