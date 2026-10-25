using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.Entities.Jobs;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Probes storage profiles for connection health so uploads are never dispatched to
    /// a drive with a revoked token or an exhausted quota.
    /// </summary>
    public interface IStorageProfileHealthService
    {
        /// <summary>
        /// Health of a single profile. Cached in Redis for a short window; pass
        /// <paramref name="forceRefresh"/> to bypass the cache and hit the provider.
        /// </summary>
        Task<StorageProfileHealthDto> GetHealthAsync(
            UserStorageProfile profile,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);

        /// <summary>Health of one of the user's profiles, by id.</summary>
        Task<StorageProfileHealthDto> GetHealthAsync(
            int userId,
            int profileId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);

        /// <summary>Health of every active profile the user owns.</summary>
        Task<List<StorageProfileHealthDto>> GetHealthForUserAsync(
            int userId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// True when the profile can accept an upload of <paramref name="requiredBytes"/>.
        /// Uses the cached probe unless it has expired.
        /// </summary>
        Task<bool> IsUsableAsync(
            UserStorageProfile profile,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records an upload failure against the profile. Hard errors (revoked token,
        /// quota exhausted) mark it Unhealthy immediately; anything else increments the
        /// consecutive-failure counter and demotes the profile once the threshold is hit.
        /// </summary>
        Task RecordFailureAsync(
            UserStorageProfile profile,
            Exception exception,
            CancellationToken cancellationToken = default);

        /// <summary>Clears failure state after a successful upload.</summary>
        Task RecordSuccessAsync(
            UserStorageProfile profile,
            CancellationToken cancellationToken = default);

        /// <summary>Drops the cached probe result so the next read hits the provider.</summary>
        Task InvalidateAsync(int profileId);
    }
}
