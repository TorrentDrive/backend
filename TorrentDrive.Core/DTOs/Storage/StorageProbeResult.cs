using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.DTOs.Storage
{
    /// <summary>
    /// Raw outcome of a provider-level connection probe, before it is merged with the
    /// profile's persisted health state.
    /// </summary>
    public class StorageProbeResult
    {
        public StorageHealthStatus Status { get; set; } = StorageHealthStatus.Unknown;

        /// <summary>Machine-readable reason code when the probe did not return Healthy.</summary>
        public string? Reason { get; set; }

        public string? Message { get; set; }

        public long? QuotaTotalBytes { get; set; }
        public long? QuotaUsedBytes { get; set; }

        /// <summary>True when the failure will not clear without user action (re-auth, quota).</summary>
        public bool RequiresUserAction { get; set; }

        public static StorageProbeResult Healthy(long? total = null, long? used = null) =>
            new() { Status = StorageHealthStatus.Healthy, QuotaTotalBytes = total, QuotaUsedBytes = used };

        public static StorageProbeResult Unhealthy(string reason, string message, bool requiresUserAction = false) =>
            new()
            {
                Status = StorageHealthStatus.Unhealthy,
                Reason = reason,
                Message = message,
                RequiresUserAction = requiresUserAction
            };

        public static StorageProbeResult Degraded(string reason, string message, long? total = null, long? used = null) =>
            new()
            {
                Status = StorageHealthStatus.Degraded,
                Reason = reason,
                Message = message,
                QuotaTotalBytes = total,
                QuotaUsedBytes = used
            };
    }
}
