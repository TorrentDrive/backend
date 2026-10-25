using TorrentDrive.Core.DTOs.Auth;

namespace TorrentDrive.Core.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginAsync(string email, string password);

        /// <summary>
        /// Replaces the account's password after verifying the current one.
        /// </summary>
        Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    }
}
