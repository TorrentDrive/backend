using Microsoft.AspNetCore.Identity;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services.Setup
{
    /// <summary>
    /// Wraps ASP.NET Identity's <see cref="PasswordHasher{TUser}"/> — PBKDF2 with a
    /// versioned output format, a per-password salt and a constant-time comparison.
    /// Deliberately not hand-rolled.
    /// </summary>
    public class PasswordHasherService : IPasswordHasher
    {
        private readonly PasswordHasher<User> _hasher = new();

        public string Hash(string password) => _hasher.HashPassword(new User(), password);

        public bool Verify(string? hash, string password)
        {
            // No stored hash means the account has no password of its own. That is a failed
            // login, not an exception — the caller may still have a legacy path to try.
            if (string.IsNullOrEmpty(hash)) return false;

            var result = _hasher.VerifyHashedPassword(new User(), hash, password);

            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
    }
}
