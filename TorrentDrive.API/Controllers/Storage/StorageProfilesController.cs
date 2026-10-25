using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers.Storage
{
    [Route("api/storage")]
    [Authorize]
    public class StorageProfilesController(
        IStorageProfilesService storageProfilesService,
        IStorageProfileHealthService storageProfileHealthService
        ) : BaseApiController
    {
        [HttpGet("profiles")]
        public async Task<IActionResult> GetStorageProfiles()
            => Ok(await storageProfilesService.GetStorageProfilesAsync(UserId));

        [HttpGet("profiles/{id:int}")]
        public async Task<IActionResult> GetStorageProfile(int id)
            => Ok(await storageProfilesService.GetStorageProfileAsync(UserId, id));

        /// <summary>
        /// Connection health for every active profile. Served from a short-lived cache
        /// unless <paramref name="refresh"/> is set, which forces a live provider call.
        /// </summary>
        [HttpGet("profiles/health")]
        public async Task<IActionResult> GetStorageProfilesHealth([FromQuery] bool refresh = false, CancellationToken ct = default)
            => Ok(await storageProfileHealthService.GetHealthForUserAsync(UserId, refresh, ct));

        /// <summary>Connection health for a single profile.</summary>
        [HttpGet("profiles/{id:int}/health")]
        public async Task<IActionResult> GetStorageProfileHealth(int id, [FromQuery] bool refresh = false, CancellationToken ct = default)
            => Ok(await storageProfileHealthService.GetHealthAsync(UserId, id, refresh, ct));

        /// <summary>Runs a live connection test against the profile, bypassing the cache.</summary>
        [HttpPost("profiles/{id:int}/health/check")]
        public async Task<IActionResult> CheckStorageProfileHealth(int id, CancellationToken ct = default)
            => Ok(await storageProfileHealthService.GetHealthAsync(UserId, id, forceRefresh: true, ct));

        [HttpPost("profiles/{id:int}/set-default")]
        public async Task<IActionResult> SetDefaultProfile(int id)
        {
            await storageProfilesService.SetDefaultProfileAsync(UserId, id);
            return Ok();
        }

        [HttpPost("profiles/{id:int}/disconnect")]
        public async Task<IActionResult> DisconnectProfile(int id)
        {
            await storageProfilesService.DisconnectProfileAsync(UserId, id);
            return Ok();
        }

        [HttpDelete("profiles/{id:int}")]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            await storageProfilesService.DeleteStorageProfileAsync(UserId, id);
            return NoContent();
        }
    }
}
