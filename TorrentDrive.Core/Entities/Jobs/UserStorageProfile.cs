using System.ComponentModel.DataAnnotations.Schema;
using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Entities.Jobs
{
    public class UserStorageProfile : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public string ProfileName { get; set; } = string.Empty;

        public StorageProviderType ProviderType { get; set; }
        public string? Email { get; set; } // Email associated with the storage account (nullable for non-email providers)
        public string CredentialsJson { get; set; } = "{}";

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Set to true when Google refresh token is expired/revoked.
        /// Cleared after successful re-authentication.
        /// </summary>
        public bool NeedsReauth { get; set; } = false;

        /// <summary>
        /// FK to the reusable OAuth app credentials (ClientId/Secret/RedirectUri).
        /// Null for non-Google-Drive providers or legacy profiles.
        /// </summary>
        public int? OAuthCredentialId { get; set; }
        public UserOAuthCredential? OAuthCredential { get; set; }

        // --- Connection health (see IStorageProfileHealthService) ---

        /// <summary>
        /// Result of the last health probe. Uploads are only routed to Healthy or
        /// Degraded profiles; Unhealthy ones trigger failover.
        /// </summary>
        public StorageHealthStatus HealthStatus { get; set; } = StorageHealthStatus.Unknown;

        /// <summary>When the last probe ran. Null if never probed.</summary>
        public DateTime? LastHealthCheckAt { get; set; }

        /// <summary>Message from the last failed probe or upload. Null when healthy.</summary>
        public string? LastHealthError { get; set; }

        /// <summary>
        /// Upload failures in a row against this profile. Reset on any success.
        /// Used to demote a profile that keeps failing without a hard error.
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>Total account quota in bytes, as reported by the provider. Null if unlimited/unknown.</summary>
        public long? QuotaTotalBytes { get; set; }

        /// <summary>Used account quota in bytes, as reported by the provider. Null if unknown.</summary>
        public long? QuotaUsedBytes { get; set; }

        /// <summary>Free bytes, or null when the provider does not report a quota.</summary>
        [NotMapped]
        public long? QuotaFreeBytes =>
            QuotaTotalBytes.HasValue && QuotaUsedBytes.HasValue
                ? Math.Max(0, QuotaTotalBytes.Value - QuotaUsedBytes.Value)
                : null;
    }
}
