using TorrentDrive.Core.DTOs.Auth;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// First-run setup. Claiming an unconfigured instance is the one action here, and it
    /// is anonymous by necessity — there is no account to authenticate against yet — so
    /// the implementation is responsible for making sure it can only ever succeed once.
    /// </summary>
    public interface ISetupService
    {
        Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the admin account and marks setup complete.
        /// Throws <see cref="Exceptions.ConflictException"/> if setup already ran.
        /// </summary>
        Task<AuthResponseDto> CreateAdminAsync(
            CreateAdminRequestDto request,
            CancellationToken cancellationToken = default);
    }
}
