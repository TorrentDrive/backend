using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.DTOs.Storage
{
    public class StorageProfileDetailDto
    {
        public int Id { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public string? Email { get; set; } // Email associated with the storage account
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public bool NeedsReauth { get; set; }
        /// <summary>True when the profile has completed OAuth and has a refresh token.</summary>
        public bool IsConfigured { get; set; }

        /// <summary>Last known connection health. See IStorageProfileHealthService.</summary>
        public StorageHealthStatus HealthStatus { get; set; } = StorageHealthStatus.Unknown;

        /// <summary>True when this profile can accept an upload right now.</summary>
        public bool IsUsable { get; set; } = true;

        /// <summary>Why the profile is not healthy. Null when it is.</summary>
        public string? HealthMessage { get; set; }

        public DateTime? LastHealthCheckAt { get; set; }

        public long? QuotaTotalBytes { get; set; }
        public long? QuotaUsedBytes { get; set; }
        public long? QuotaFreeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
