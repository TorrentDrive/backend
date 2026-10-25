using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.DTOs.Storage
{
    /// <summary>
    /// Result of a connection health probe against a single storage profile.
    /// </summary>
    public class StorageProfileHealthDto
    {
        public int ProfileId { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public string? Email { get; set; }

        public StorageHealthStatus Status { get; set; } = StorageHealthStatus.Unknown;

        /// <summary>True when the profile can accept an upload right now.</summary>
        public bool IsUsable { get; set; }

        /// <summary>Machine-readable reason when not healthy (e.g. "NeedsReauth", "QuotaExceeded").</summary>
        public string? Reason { get; set; }

        /// <summary>Human-readable detail for the UI. Null when healthy.</summary>
        public string? Message { get; set; }

        public bool NeedsReauth { get; set; }
        public int ConsecutiveFailures { get; set; }

        public long? QuotaTotalBytes { get; set; }
        public long? QuotaUsedBytes { get; set; }
        public long? QuotaFreeBytes { get; set; }

        public DateTime? CheckedAt { get; set; }

        /// <summary>True when this result came from cache rather than a live provider call.</summary>
        public bool FromCache { get; set; }
    }
}
