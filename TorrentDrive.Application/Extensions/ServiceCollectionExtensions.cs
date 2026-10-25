using Microsoft.Extensions.DependencyInjection;
using TorrentDrive.Application.Services;
using TorrentDrive.Application.Services.OAuth;
using TorrentDrive.Application.Services.Setup;
using TorrentDrive.Application.Services.Storage;
using TorrentDrive.Application.Services.Torrent;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITorrentAnalysisService, TorrentAnalysisService>();
            services.AddScoped<ITrackerScraper, UdpTrackerScraper>();
            services.AddScoped<ITorrentService, TorrentService>();
            services.AddScoped<ITorrentHealthService, TorrentHealthService>();

            services.AddScoped<IJobService, JobService>();
            services.AddScoped<IGoogleDriveAuthService, GoogleDriveAuthService>();
            services.AddScoped<IOAuthStateService, OAuthStateService>();

            services.AddScoped<IStorageProfilesService, StorageProfilesService>();
            services.AddScoped<IS3StorageService, S3StorageService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IUserSettingsService, UserSettingsService>();

            // Instance-wide settings and first-run setup. The cache is a singleton so every
            // scope in the process shares one copy of a row that is read constantly and
            // written rarely.
            services.AddSingleton<SystemSettingsCache>();
            services.AddSingleton<IPasswordHasher, PasswordHasherService>();
            services.AddScoped<ISystemSettingsService, SystemSettingsService>();
            services.AddScoped<ISetupService, SetupService>();
            services.AddScoped<IOAuthService, OAuthService>();

            return services;
        }
    }
}
