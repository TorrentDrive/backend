using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorrentDrive.Core.DTOs.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.API.Controllers
{
    [Route("api/jobs")]
    [Authorize]
    public class JobsController(IJobService jobService, IJobStatusService jobStatusService) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetJobs([FromQuery] JobQueryParametersDto query)
        {
            return Ok(await jobService.GetUserJobsAsync(UserId, query));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(int id)
        {
            return Ok(await jobService.GetJobByIdAsync(UserId, id));
        }

        /// <summary>
        /// Get the full status timeline for a specific job.
        /// </summary>
        [HttpGet("{id}/timeline")]
        public async Task<IActionResult> GetJobTimeline(int id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            // Verify the user has access to this job (throws NotFoundException if not found)
            await jobService.GetJobByIdAsync(UserId, id);

            var timeline = await jobStatusService.GetJobTimelinePaginatedAsync(id, pageNumber, pageSize);
            return Ok(timeline);
        }

        /// <summary>
        /// Worker capacity and queue depth. Lets the UI say whether a queued job is
        /// waiting its turn or was dropped, instead of guessing.
        /// </summary>
        [HttpGet("queue-status")]
        public async Task<IActionResult> GetQueueStatus()
        {
            return Ok(await jobService.GetQueueStatusAsync());
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetJobStatistics()
        {
            return Ok(await jobService.GetUserJobStatisticsAsync(UserId));
        }

        /// <summary>
        /// Retries all retryable failed jobs for the user, or the subset specified in the request body.
        /// </summary>
        [HttpPost("batch/retry-failed")]
        public async Task<IActionResult> BatchRetryFailedJobs([FromBody] BatchJobIdsRequestDto? request = null)
        {
            var result = await jobService.RetryFailedJobsAsync(UserId, request);
            return Ok(result);
        }

        /// <summary>
        /// Cancels the jobs specified in the request body (or all cancellable jobs if none specified).
        /// </summary>
        [HttpPost("batch/cancel")]
        public async Task<IActionResult> BatchCancelJobs([FromBody] BatchJobIdsRequestDto? request = null)
        {
            var result = await jobService.CancelJobsAsync(UserId, request ?? new BatchJobIdsRequestDto());
            return Ok(result);
        }

        /// <summary>
        /// Retries a job. An optional body redirects the retry to a different drive.
        /// </summary>
        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(int id, [FromBody] RetryJobRequestDto? request = null)
        {
            await jobService.RetryJobAsync(id, UserId, targetStorageProfileId: request?.StorageProfileId);
            return Ok();
        }

        /// <summary>
        /// Points a job at a different storage profile before its upload runs. If the job
        /// is already waiting to upload, the queued upload is re-dispatched to the new
        /// destination.
        /// </summary>
        [HttpPatch("{id}/storage-profile")]
        public async Task<IActionResult> ChangeStorageProfile(int id, [FromBody] ChangeJobStorageProfileRequestDto request)
        {
            var job = await jobService.ChangeJobStorageProfileAsync(
                id, UserId, request.StorageProfileId, request.AllowFailover);

            return Ok(job);
        }

        /// <summary>
        /// Re-dispatches a job stuck waiting to be picked up by a worker. Valid only for
        /// QUEUED and PENDING_UPLOAD, which are the states that depend on a stream
        /// hand-off that can be lost.
        /// </summary>
        [HttpPost("{id}/force-start")]
        public async Task<IActionResult> ForceStartJob(int id)
        {
            await jobService.ForceStartJobAsync(id, UserId);
            return Ok();
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelJob(int id)
        {
            await jobService.CancelJobAsync(id, UserId);
            return Ok();
        }
    }
}
