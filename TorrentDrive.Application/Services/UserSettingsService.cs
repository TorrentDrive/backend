using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Settings;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services
{
    public class UserSettingsService(
        IUnitOfWork unitOfWork,
        ILogger<UserSettingsService> logger) : IUserSettingsService
    {
        public async Task<UserSettingsDto> GetSettingsAsync(int userId)
        {
            var settings = await GetOrCreateAsync(userId);
            return ToDto(settings);
        }

        public async Task<UserSettingsDto> UpdateSettingsAsync(int userId, UpdateUserSettingsRequestDto request)
        {
            var settings = await GetOrCreateAsync(userId);

            settings.DeleteAfterUpload = request.DeleteAfterUpload;
            await unitOfWork.Complete();

            logger.LogInformation("User settings updated | UserId: {UserId} | DeleteAfterUpload: {DeleteAfterUpload}",
                userId, settings.DeleteAfterUpload);

            return ToDto(settings);
        }

        public async Task<UserSettings> GetOrCreateAsync(int userId)
        {
            var settings = await unitOfWork.Repository<UserSettings>().FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings != null) return settings;

            // First access for this user — persist the defaults so later reads and the
            // Settings tab both see a concrete row rather than an implicit default.
            settings = new UserSettings { UserId = userId };
            unitOfWork.Repository<UserSettings>().Add(settings);
            await unitOfWork.Complete();

            logger.LogInformation("Created default settings | UserId: {UserId}", userId);
            return settings;
        }

        private static UserSettingsDto ToDto(UserSettings settings) => new()
        {
            DeleteAfterUpload = settings.DeleteAfterUpload,
        };
    }
}
