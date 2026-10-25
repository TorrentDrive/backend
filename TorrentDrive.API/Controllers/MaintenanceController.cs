using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers
{
    [Route("api/maintenance")]
    [Authorize]
    public class MaintenanceController(
        IDownloadMaintenanceService downloadMaintenanceService,
        ILogger<MaintenanceController> logger) : BaseApiController
    {
        /// <summary>
        /// What the downloads volume holds, split into what Purge would delete, what it
        /// keeps, and directories with no matching job.
        /// </summary>
        [HttpGet("downloads")]
        public async Task<IActionResult> GetDownloadStorage(CancellationToken ct = default)
            => Ok(await downloadMaintenanceService.GetPreviewAsync(UserId, ct));

        /// <summary>
        /// Deletes the download directories of COMPLETED and CANCELLED jobs. Running,
        /// retrying and failed jobs keep their files, and orphaned directories are left
        /// alone.
        /// </summary>
        [HttpPost("downloads/purge")]
        public async Task<IActionResult> PurgeDownloads(CancellationToken ct = default)
        {
            logger.LogInformation("Download purge requested | UserId: {UserId}", UserId);

            var result = await downloadMaintenanceService.PurgeAsync(UserId, ct);

            logger.LogInformation("Download purge finished | UserId: {UserId} | Deleted: {Deleted} | Freed: {FreedMB:F2} MB",
                UserId, result.DeletedCount, result.FreedBytes / (1024.0 * 1024.0));

            return Ok(result);
        }
    }
}
