using TorrentDrive.Core.DTOs.OAuth;
using TorrentDrive.Core.DTOs.Storage.GoogleDrive;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Google Drive service — manages OAuth credentials and profile connections.
    /// </summary>
    public interface IGoogleDriveService
    {
        /// <summary>
        /// Save reusable OAuth app credentials.
        /// </summary>
        Task<SavedCredentialsDto> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request);

        /// <summary>
        /// List saved OAuth app credentials for a user.
        /// </summary>
        Task<List<OAuthCredentialDto>> GetCredentialsAsync(int userId);

        /// <summary>
        /// Connect a new Google Drive account using a saved credential.
        /// </summary>
        Task<string> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request);

        /// <summary>
        /// Re-initiate OAuth for a profile with expired refresh token.
        /// </summary>
        Task<string> ReauthenticateAsync(int userId, int profileId);

        /// <summary>
        /// Handle OAuth callback from Google and redirect to frontend.
        /// Always returns a redirect URL, never throws.
        /// </summary>
        /// <param name="frontendOrigin">
        /// Origin the callback arrived through, as observed by the API. Google redirects the
        /// browser to the frontend, which proxies the request here, so this is the address
        /// the user is actually on — which is why the instance does not need to be told its
        /// own URL. Ignored when FRONTEND_URL is set explicitly.
        /// </param>
        Task<string> GetGoogleCallback(string code, string state, string? frontendOrigin = null);
    }
}
