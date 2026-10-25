using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.DTOs.Torrents;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers.Torrents
{
    [Authorize]
    [ApiController]
    [Route("api/torrents")]
    public class TorrentFileController(
        IJobService jobService,
        ILogger<TorrentFileController> logger) : BaseApiController
    {
      
        [HttpPost("create-job")]
 
        public async Task<IActionResult> CreateJobAsync([FromBody] CreateJobRequestDto request)
        {
            var userId = GetCurrentUserId();

            logger.LogInformation("Create job requested | TorrentFileId: {TorrentFileId} | UserId: {UserId}", request.TorrentFileId, userId);

            var result = await jobService.CreateAndDispatchJobAsync(
                request.TorrentFileId,
                userId,
                request.SelectedFilePaths,
                request.StorageProfileId);

            logger.LogInformation("Job created successfully | JobId: {JobId} | TorrentFileId: {TorrentFileId} | UserId: {UserId}",
                result.JobId, request.TorrentFileId, userId);

            return Ok(result);
        }

        /// <summary>
        /// Starts several analysed torrents at once. Every item is reported back
        /// individually, so one rejected torrent does not stop the others.
        /// </summary>
        [HttpPost("create-jobs")]
        public async Task<IActionResult> CreateJobsAsync([FromBody] CreateJobsRequestDto request)
        {
            var userId = GetCurrentUserId();

            logger.LogInformation("Batch job creation requested | Items: {Count} | UserId: {UserId}",
                request.Items?.Count ?? 0, userId);

            var result = await jobService.CreateAndDispatchJobsAsync(userId, request);

            logger.LogInformation("Batch job creation finished | Succeeded: {Succeeded} | Failed: {Failed} | UserId: {UserId}",
                result.SucceededCount, result.FailedCount, userId);

            return Ok(result);
        }
    }
}
