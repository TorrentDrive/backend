using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.OAuth;
using TorrentDrive.Core.DTOs.Storage.GoogleDrive;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services.Google_Drive
{
    /// <summary>
    /// Google Drive service implementation — manages OAuth credentials and profile connections.
    /// </summary>
    public class GoogleDriveService(
        IGoogleDriveAuthService googleDriveAuthService,
        IConfiguration configuration,
        ILogger<GoogleDriveService> logger) : IGoogleDriveService
    {
        public Task<SavedCredentialsDto> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request)
            => googleDriveAuthService.SaveCredentialsAsync(userId, request);

        public Task<List<OAuthCredentialDto>> GetCredentialsAsync(int userId)
            => googleDriveAuthService.GetCredentialsAsync(userId);

        public Task<string> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request)
            => googleDriveAuthService.ConnectAsync(userId, request);

        public Task<string> ReauthenticateAsync(int userId, int profileId)
            => googleDriveAuthService.ReauthenticateAsync(userId, profileId);

        public async Task<string> GetGoogleCallback(string code, string state, string? frontendOrigin = null)
        {
            var redirectBase = $"{ResolveFrontendUrl(frontendOrigin)}/storage";

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return $"{redirectBase}?error=INVALID_REQUEST&message={HttpUtility.UrlEncode("Missing code or state parameter")}";

            try
            {
                var profileId = await googleDriveAuthService.HandleOAuthCallbackAsync(code, state);
                return $"{redirectBase}?success=true&profileId={profileId}";
            }
            catch (DomainException ex)
            {
                return $"{redirectBase}?error={HttpUtility.UrlEncode(ex.Code)}&message={HttpUtility.UrlEncode(ex.Message)}";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in GetGoogleCallback");
                return $"{redirectBase}?error=InternalError&message={HttpUtility.UrlEncode("An unexpected error occurred")}";
            }
        }

        /// <summary>
        /// Where to send the browser once Google has handed the code back.
        ///
        /// Configuration wins, for deployments behind a proxy that rewrites the host. With
        /// nothing configured the origin the callback came in on is used, which is what lets
        /// a container run without being told its own public address. Only the origin is
        /// taken from the request — never a path — so a forged Host header can at worst
        /// redirect the forger's own browser.
        /// </summary>
        private string ResolveFrontendUrl(string? frontendOrigin)
        {
            var configured = configuration["FRONTEND_URL"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(frontendOrigin)
                && Uri.TryCreate(frontendOrigin, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                return $"{uri.Scheme}://{uri.Authority}";
            }

            return "http://localhost:47100";
        }
    }
}
