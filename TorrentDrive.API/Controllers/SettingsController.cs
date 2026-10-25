using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.DTOs.Auth;
using TorrentDrive.Core.DTOs.Settings;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers
{
    [Route("api/settings")]
    [Authorize]
    public class SettingsController(
        IUserSettingsService userSettingsService,
        ISystemSettingsService systemSettingsService,
        IAuthService authService) : BaseApiController
    {
        /// <summary>Current preferences, created with defaults on first read.</summary>
        [HttpGet]
        public async Task<IActionResult> GetSettings()
            => Ok(await userSettingsService.GetSettingsAsync(UserId));

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] UpdateUserSettingsRequestDto request)
            => Ok(await userSettingsService.UpdateSettingsAsync(UserId, request));

        /// <summary>
        /// Instance-wide settings. Single-admin instance, so every authenticated caller is
        /// the owner; this needs an admin check the day a second account can exist.
        /// </summary>
        [HttpGet("system")]
        public async Task<IActionResult> GetSystemSettings(CancellationToken cancellationToken)
            => Ok(await systemSettingsService.GetSettingsAsync(cancellationToken));

        [HttpPut("system")]
        public async Task<IActionResult> UpdateSystemSettings(
            [FromBody] UpdateSystemSettingsRequestDto request,
            CancellationToken cancellationToken)
            => Ok(await systemSettingsService.UpdateSettingsAsync(request, cancellationToken));

        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
        {
            await authService.ChangePasswordAsync(UserId, request.CurrentPassword, request.NewPassword);
            return NoContent();
        }
    }
}
