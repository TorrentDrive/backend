using TorrentDrive.Core.DTOs.Settings;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Options;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Reads and writes the single instance-wide settings row, creating it with defaults
    /// (seeded from configuration) on first access so callers never handle a missing record.
    ///
    /// Reads are served from a short-lived process cache: these values are consulted on
    /// every upload-routing decision, and a database round-trip per decision would be
    /// wasteful for a row that changes a handful of times in an install's lifetime.
    /// </summary>
    public interface ISystemSettingsService
    {
        Task<SystemSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

        Task<SystemSettingsDto> UpdateSettingsAsync(
            UpdateSystemSettingsRequestDto request,
            CancellationToken cancellationToken = default);

        /// <summary>Returns the settings entity, creating it if absent.</summary>
        Task<SystemSettings> GetOrCreateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The upload-routing subset, shaped as the options object the routing services
        /// already consume.
        /// </summary>
        Task<UploadRoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Same as <see cref="GetRoutingOptionsAsync"/> but never touches the database:
        /// returns the cached value, or configuration defaults if nothing is cached yet.
        /// Exists for the options pipeline, which has no async entry point.
        /// </summary>
        UploadRoutingOptions GetCachedRoutingOptions();
    }
}
