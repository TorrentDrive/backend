using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrenClou.Core.DTOs.Auth;
using TorrenClou.Core.Entities;
using TorrenClou.Core.Exceptions;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Application.Services.Setup
{
    /// <summary>
    /// First-run setup: claiming an unconfigured instance.
    ///
    /// This is the only anonymous write in the application, so the guard against running it
    /// twice is the security boundary for the whole install — without it, anyone who can
    /// reach the instance before its owner does becomes its admin.
    /// </summary>
    public class SetupService(
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ISystemSettingsService systemSettingsService,
        ILogger<SetupService> logger) : ISetupService
    {
        /// <summary>
        /// Minimum admin password length. This account owns the instance and its cloud
        /// credentials, and there is no rate limiting in front of the login endpoint.
        /// </summary>
        private const int MinPasswordLength = 12;

        /// <summary>
        /// Serialises the check-and-claim so two simultaneous requests cannot both pass the
        /// "not set up yet" check. Only the API process serves setup, so a process-level
        /// gate covers it; the database check inside is what makes a second API replica
        /// merely unlikely to race rather than guaranteed to.
        /// </summary>
        private static readonly SemaphoreSlim ClaimGate = new(1, 1);

        public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
            => new() { NeedsSetup = await NeedsSetupAsync(cancellationToken) };

        public async Task<AuthResponseDto> CreateAdminAsync(
            CreateAdminRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Validate(request);

            await ClaimGate.WaitAsync(cancellationToken);
            try
            {
                // Re-checked inside the gate, not before it: the value can change between a
                // caller's status request and this call.
                if (!await NeedsSetupAsync(cancellationToken))
                {
                    logger.LogWarning("Rejected setup attempt on an instance that is already configured");

                    throw new ConflictException("SetupAlreadyComplete",
                        "This instance has already been set up.");
                }

                var email = request.Email.Trim();

                var user = await FindUserByEmailAsync(email);
                if (user == null)
                {
                    user = new User
                    {
                        Email = email,
                        FullName = request.FullName.Trim(),
                        PasswordHash = passwordHasher.Hash(request.Password)
                    };
                    unitOfWork.Repository<User>().Add(user);
                }
                else
                {
                    // A user row can already exist without a password: the pre-setup admin
                    // path created one on first login. Adopt it rather than orphaning the
                    // jobs and storage profiles that point at it.
                    user.FullName = request.FullName.Trim();
                    user.PasswordHash = passwordHasher.Hash(request.Password);
                }

                var settings = await systemSettingsService.GetOrCreateAsync(cancellationToken);
                settings.SetupCompletedAt = DateTime.UtcNow;

                await unitOfWork.Complete();

                logger.LogInformation("Setup completed | AdminUserId: {UserId}", user.Id);

                return new AuthResponseDto
                {
                    AccessToken = tokenService.CreateToken(user),
                    Email = user.Email,
                    FullName = user.FullName
                };
            }
            finally
            {
                ClaimGate.Release();
            }
        }

        // --- Helpers ---

        /// <summary>
        /// Setup is needed only when the instance has never been claimed and there is no
        /// other way in. An install still using ADMIN_EMAIL/ADMIN_PASSWORD is already
        /// reachable by its owner, so offering it the wizard would hand it to a stranger.
        /// </summary>
        private async Task<bool> NeedsSetupAsync(CancellationToken cancellationToken)
        {
            var settings = await systemSettingsService.GetOrCreateAsync(cancellationToken);
            if (settings.SetupCompletedAt != null) return false;

            if (HasLegacyAdminConfigured()) return false;

            // Belt and braces: a password on any account means someone has already claimed
            // this instance, whatever the settings row says.
            return !await AnyUserHasPasswordAsync();
        }

        private bool HasLegacyAdminConfigured()
            => !string.IsNullOrEmpty(configuration["ADMIN_EMAIL"])
            && !string.IsNullOrEmpty(configuration["ADMIN_PASSWORD"]);

        private async Task<bool> AnyUserHasPasswordAsync()
        {
            return await unitOfWork.Repository<User>().FirstOrDefaultAsync(u => u.PasswordHash != null) != null;
        }

        private async Task<User?> FindUserByEmailAsync(string email)
        {
            return await unitOfWork.Repository<User>().FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        private static void Validate(CreateAdminRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                throw new ValidationException("InvalidEmail", "Enter a valid email address.");

            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new ValidationException("InvalidName", "Enter a name for the account.");

            if (request.Password.Length < MinPasswordLength)
                throw new ValidationException("WeakPassword",
                    $"Password must be at least {MinPasswordLength} characters.");
        }
    }
}
