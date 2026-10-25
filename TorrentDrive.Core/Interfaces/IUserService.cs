using TorrentDrive.Core.Entities;

namespace TorrentDrive.Core.Interfaces
{
    public interface IUserService
    {
        Task<bool> UserExistsAsync(string email);
        Task<User> CreateUser(string email, string name);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(int userId);

        /// <summary>Stores an already-hashed password. Never takes a plaintext value.</summary>
        Task SetPasswordHashAsync(User user, string passwordHash);
    }
}
