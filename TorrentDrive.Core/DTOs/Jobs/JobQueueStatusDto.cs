namespace TorrentDrive.Core.DTOs.Jobs
{
    /// <summary>
    /// How busy the download and upload workers are.
    ///
    /// A download job holds its Hangfire worker for the whole transfer, so worker count
    /// is a hard ceiling on concurrent torrents. Without this, a job waiting for a free
    /// slot is indistinguishable from one whose queue hand-off was lost — and only the
    /// second is worth force starting.
    /// </summary>
    public record JobQueueStatusDto
    {
        /// <summary>Worker slots across every server consuming the torrents queue.</summary>
        public int DownloadCapacity { get; init; }

        /// <summary>Jobs currently downloading, counted from our own records.</summary>
        public int ActiveDownloads { get; init; }

        /// <summary>Jobs waiting for a download slot.</summary>
        public int QueuedDownloads { get; init; }

        /// <summary>Worker slots across every server consuming an upload queue.</summary>
        public int UploadCapacity { get; init; }

        public int ActiveUploads { get; init; }

        /// <summary>
        /// True when every download slot is taken. While this holds, a queued job is
        /// waiting its turn rather than stuck, and re-dispatching it changes nothing.
        /// </summary>
        public bool DownloadSlotsFull => DownloadCapacity > 0 && ActiveDownloads >= DownloadCapacity;

        /// <summary>
        /// Null when capacity could not be read (no Hangfire server reporting). Callers
        /// should then avoid claiming anything about why a job is waiting.
        /// </summary>
        public bool? IsCapacityKnown => DownloadCapacity > 0 ? true : null;
    }
}
