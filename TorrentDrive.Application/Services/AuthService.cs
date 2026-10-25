using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Auth;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services
{
    /// <summary>
    /// Password login against the database.
    ///
    /// Credentials used to live in ADMIN_EMAIL/ADMIN_PASSWORD and were compared in
    /// plaintext. They now live on the user row as a hash, set by the first-run wizard.
    /// The environment variables are still honoured as a one-time bootstrap so an install
    /// that upgrades into this version can still log in with what it already had.
    /// </summary>
    public class AuthService(
        IConfiguration configuration,
        ITokenService tokenService,
        IUserService userService,
        IPasswordHasher passwordHasher,
        ILogger<AuthService> logger
        ) : IAuthService
    {
        public async Task<AuthResponseDto> LoginAsync(string email, string password)
        {
            var user = await userService.GetUserByEmailAsync(email);

            if (user?.PasswordHash != null)
            {
                if (!passwordHasher.Verify(user.PasswordHash, password))
                    await RejectAsync();

                return Respond(user);
            }

            // No password on file for this account. The only remaining way in is the legacy
            // environment admin, which then gets upgraded to a hash so this branch is taken
            // at most once per install.
            var upgraded = await TryLegacyAdminLoginAsync(email, password, user);
            if (upgraded == null) await RejectAsync();

            return Respond(upgraded!);
        }

        public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            if (newPassword.Length < 12)
                throw new ValidationException("WeakPassword", "Password must be at least 12 characters.");

            var user = await userService.GetUserByIdAsync(userId)
                ?? throw new NotFoundException("UserNotFound", "Account not found.");

            // An account created by the legacy admin path has no hash to verify against.
            // Fall back to the configured password so its owner can still set a real one.
            var verified = user.PasswordHash != null
                ? passwordHasher.Verify(user.PasswordHash, currentPassword)
                : LegacyPasswordMatches(user.Email, currentPassword);

            if (!verified)
            {
                await Task.Delay(100);
                throw new UnauthorizedException("InvalidCredentials", "Current password is incorrect.");
            }

            await userService.SetPasswordHashAsync(user, passwordHasher.Hash(newPassword));

            logger.LogInformation("Password changed | UserId: {UserId}", user.Id);
        }

        // --- Helpers ---

        /// <summary>
        /// Accepts ADMIN_EMAIL/ADMIN_PASSWORD once and converts that account to a hashed
        /// password, so an upgrading install keeps working without the plaintext comparison
        /// surviving past the first successful login.
        /// </summary>
        private async Task<User?> TryLegacyAdminLoginAsync(string email, string password, User? existing)
        {
            if (!LegacyPasswordMatches(email, password)) return null;

            var name = configuration["ADMIN_NAME"] ?? "TorrentDrive Admin";
            var user = existing ?? await userService.CreateUser(email, name);

            await userService.SetPasswordHashAsync(user, passwordHasher.Hash(password));

            logger.LogWarning(
                "Logged in with the environment admin credentials and converted the account to a stored password. "
                + "ADMIN_EMAIL and ADMIN_PASSWORD can now be removed | UserId: {UserId}", user.Id);

            return user;
        }

        private bool LegacyPasswordMatches(string email, string password)
        {
            var adminEmail = configuration["ADMIN_EMAIL"];
            var adminPassword = configuration["ADMIN_PASSWORD"];

            if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
                return false;

            return email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase)
                && password == adminPassword;
        }

        /// <summary>Delays before failing so a wrong password is not distinguishable by timing.</summary>
        private static async Task RejectAsync()
        {
            await Task.Delay(100);
            throw new UnauthorizedException("InvalidCredentials", "Invalid email or password");
        }

        private AuthResponseDto Respond(User user) => new()
        {
            AccessToken = tokenService.CreateToken(user),
            Email = user.Email,
            FullName = user.FullName
        };
    }
}
