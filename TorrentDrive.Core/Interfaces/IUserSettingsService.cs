using TorrentDrive.Core.DTOs.Settings;
using TorrentDrive.Core.Entities;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Reads and writes per-user preferences, creating the row with defaults on first
    /// access so callers never have to handle a missing settings record.
    /// </summary>
    public interface IUserSettingsService
    {
        Task<UserSettingsDto> GetSettingsAsync(int userId);

        Task<UserSettingsDto> UpdateSettingsAsync(int userId, UpdateUserSettingsRequestDto request);

        /// <summary>
        /// Returns the settings entity, creating it if absent. Used by workers that need
        /// the raw values rather than the DTO.
        /// </summary>
        Task<UserSettings> GetOrCreateAsync(int userId);
    }
}
