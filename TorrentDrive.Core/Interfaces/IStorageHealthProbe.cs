using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Provider-specific connection check. One implementation per
    /// <see cref="StorageProviderType"/>; resolved by <see cref="IStorageProfileHealthService"/>.
    /// </summary>
    public interface IStorageHealthProbe
    {
        StorageProviderType ProviderType { get; }

        /// <summary>
        /// Calls the provider to verify credentials and read quota. Implementations must
        /// not throw: network and auth errors come back as an Unhealthy result.
        /// </summary>
        Task<StorageProbeResult> ProbeAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);

        /// <summary>
        /// Classifies an upload exception thrown against this provider, so a failure can
        /// mark the profile unhealthy without a second round trip. Returns null when the
        /// exception says nothing about the profile's health.
        /// </summary>
        StorageProbeResult? ClassifyFailure(Exception exception);
    }
}
