using System.Text.Json;
using TorrenClou.Core.DTOs.Storage;
using TorrenClou.Core.DTOs.Storage.GoogleDrive;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Exceptions;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Application.Services.Storage
{
    public class StorageProfilesService(IUnitOfWork unitOfWork, IJobService jobService) : IStorageProfilesService
    {
        public async Task<List<StorageProfileDto>> GetStorageProfilesAsync(int userId)
        {
            var profiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(
                p => p.UserId == userId && p.IsActive);

            return profiles
                .OrderBy(p => p.IsDefault ? 0 : 1)
                .ThenBy(p => p.CreatedAt)
                .Select(p => new StorageProfileDto
                {
                    Id = p.Id,
                    ProfileName = p.ProfileName,
                    ProviderType = p.ProviderType.ToString(),
                    Email = p.Email,
                    IsDefault = p.IsDefault,
                    IsActive = p.IsActive,
                    NeedsReauth = p.NeedsReauth,
                    IsConfigured = IsProfileConfigured(p),
                    HealthStatus = p.HealthStatus,
                    IsUsable = IsUsable(p),
                    HealthMessage = p.LastHealthError,
                    LastHealthCheckAt = p.LastHealthCheckAt,
                    QuotaTotalBytes = p.QuotaTotalBytes,
                    QuotaUsedBytes = p.QuotaUsedBytes,
                    QuotaFreeBytes = p.QuotaFreeBytes,
                    CreatedAt = p.CreatedAt
                }).ToList();
        }

        public async Task<StorageProfileDetailDto> GetStorageProfileAsync(int userId, int id)
        {
            var profile = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.Id == id && p.UserId == userId && p.IsActive);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            return new StorageProfileDetailDto
            {
                Id = profile.Id,
                ProfileName = profile.ProfileName,
                ProviderType = profile.ProviderType.ToString(),
                Email = profile.Email,
                IsDefault = profile.IsDefault,
                IsActive = profile.IsActive,
                NeedsReauth = profile.NeedsReauth,
                IsConfigured = IsProfileConfigured(profile),
                HealthStatus = profile.HealthStatus,
                IsUsable = IsUsable(profile),
                HealthMessage = profile.LastHealthError,
                LastHealthCheckAt = profile.LastHealthCheckAt,
                QuotaTotalBytes = profile.QuotaTotalBytes,
                QuotaUsedBytes = profile.QuotaUsedBytes,
                QuotaFreeBytes = profile.QuotaFreeBytes,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            };
        }

        public async Task SetDefaultProfileAsync(int userId, int id)
        {
            var profile = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.Id == id && p.UserId == userId && p.IsActive);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(
                p => p.UserId == userId && p.IsActive);

            foreach (var p in allProfiles)
                p.IsDefault = false;

            profile.IsDefault = true;
            await unitOfWork.Complete();
        }

        public async Task DisconnectProfileAsync(int userId, int id)
        {
            var profile = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.Id == id && p.UserId == userId);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            if (!profile.IsActive)
                throw new ConflictException("AlreadyDisconnected", "Storage profile is already disconnected");

            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profile.Id);

            if (activeJobs != null && activeJobs.Any())
                throw new BusinessRuleException("ProfileInUse", "Cannot disconnect profile while there are active jobs using it");

            profile.IsActive = false;

            if (profile.IsDefault)
                profile.IsDefault = false;

            await unitOfWork.Complete();
        }

        public async Task DeleteStorageProfileAsync(int userId, int profileId)
        {
            var profile = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(p => p.Id == profileId && p.UserId == userId);

            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            var activeJobs = await jobService.GetActiveJobsByStorageProfileIdAsync(profileId);
            if (activeJobs != null && activeJobs.Any())
                throw new BusinessRuleException("ProfileInUse", "Cannot delete profile while there are active jobs using it");

            profile.IsActive = false;
            if (profile.IsDefault)
                profile.IsDefault = false;

            await unitOfWork.Complete();
        }

        public async Task<UserStorageProfile?> GetDefaultStorageProfileAsync(int userId)
        {
            return await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.UserId == userId && p.IsActive && p.IsDefault);
        }

        public async Task<UserStorageProfile> Add(UserStorageProfile userStorageProfile)
        {
            unitOfWork.Repository<UserStorageProfile>().Add(userStorageProfile);
            await unitOfWork.Complete();
            return userStorageProfile;
        }

        /// <summary>
        /// Whether the last known health state allows an upload. This reads persisted
        /// state only — call IStorageProfileHealthService for a live probe.
        /// </summary>
        private static bool IsUsable(UserStorageProfile profile) =>
            profile.IsActive &&
            !profile.NeedsReauth &&
            profile.HealthStatus != StorageHealthStatus.Unhealthy;

        private static bool IsProfileConfigured(UserStorageProfile profile)
        {
            if (profile.ProviderType != StorageProviderType.GoogleDrive)
                return true;

            try
            {
                var credentials = JsonSerializer.Deserialize<GoogleDriveCredentials>(profile.CredentialsJson);
                return credentials != null && !string.IsNullOrEmpty(credentials.RefreshToken);
            }
            catch
            {
                return false;
            }
        }

        public async Task<UserStorageProfile?> GetProfileBySpecAsync(int userId, int profileId, bool activeOnly = true)
        {
            return activeOnly
                ? await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(p => p.Id == profileId && p.UserId == userId && p.IsActive)
                : await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(p => p.Id == profileId && p.UserId == userId);
        }

        public async Task<bool> HasDuplicateEmailAsync(int userId, int excludeProfileId, string email)
        {
            var duplicate = await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.UserId == userId
                    && p.Id != excludeProfileId
                    && p.ProviderType == StorageProviderType.GoogleDrive
                    && p.IsActive
                    && p.Email != null
                    && p.Email.ToLower() == email.ToLower());
            return duplicate != null;
        }

        public Task<UserStorageProfile> AddWithoutSaveAsync(UserStorageProfile profile)
        {
            unitOfWork.Repository<UserStorageProfile>().Add(profile);
            return Task.FromResult(profile);
        }

        public async Task<bool> HasDefaultProfile(int userId)
        {
            return await unitOfWork.Repository<UserStorageProfile>().FirstOrDefaultAsync(
                p => p.UserId == userId && p.IsDefault && p.IsActive) != null;
        }

        public async Task Save()
        {
            await unitOfWork.Complete();
        }
    }
}
