using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.DTOs.Auth;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers
{
    /// <summary>
    /// First-run setup. Anonymous by necessity — there is no account to authenticate
    /// against until this has run — so the service behind it is responsible for making
    /// sure the claim can only ever succeed once.
    /// </summary>
    [Route("api/setup")]
    [AllowAnonymous]
    public class SetupController(ISetupService setupService) : BaseApiController
    {
        /// <summary>
        /// Whether this instance still needs to be claimed. Returns nothing else: an
        /// anonymous caller must not learn whether an account exists or what it is called.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
            => Ok(await setupService.GetStatusAsync(cancellationToken));

        /// <summary>
        /// Creates the admin account and marks setup complete. Returns 409 on every call
        /// after the first.
        /// </summary>
        [HttpPost("admin")]
        public async Task<IActionResult> CreateAdmin(
            [FromBody] CreateAdminRequestDto request,
            CancellationToken cancellationToken)
            => Ok(await setupService.CreateAdminAsync(request, cancellationToken));
    }
}
