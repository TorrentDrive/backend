using System.Text.Json;
using Microsoft.Extensions.Logging;
using TorrenClou.Core.DTOs.Common;
using TorrenClou.Core.DTOs.Jobs;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Infrastructure.Services
{
    /// <summary>
    /// Service for managing job and sync status transitions with full audit trail.
    /// </summary>
    public class JobStatusService(
        IUnitOfWork unitOfWork,
        ILogger<JobStatusService> logger) : IJobStatusService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <inheritdoc />
        public async Task TransitionJobStatusAsync(
            UserJob job,
            JobStatus newStatus,
            StatusChangeSource source,
            string? errorMessage = null,
            object? metadata = null)
        {
            var fromStatus = job.Status;

            // Skip if status hasn't changed (unless there's an error message to record)
            if (fromStatus == newStatus && string.IsNullOrEmpty(errorMessage))
            {
                logger.LogDebug("Job {JobId} status unchanged at {Status}, skipping history entry", job.Id, newStatus);
                return;
            }

            // Create history entry
            var historyEntry = new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = fromStatus,
                ToStatus = newStatus,
                Source = source,
                ErrorMessage = errorMessage,
                MetadataJson = metadata != null ? JsonSerializer.Serialize(metadata, JsonOptions) : null,
                ChangedAt = DateTime.UtcNow
            };

            // Update job status and error message
            job.Status = newStatus;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                job.ErrorMessage = errorMessage;
            }

            // Add history entry
            unitOfWork.Repository<JobStatusHistory>().Add(historyEntry);

            // Save changes
            await unitOfWork.Complete();

            logger.LogInformation(
                "Job {JobId} transitioned from {FromStatus} to {ToStatus} | Source: {Source}",
                job.Id, fromStatus, newStatus, source);
        }


        /// <inheritdoc />
        public async Task RecordJobEventAsync(
            UserJob job,
            string message,
            StatusChangeSource source,
            object? metadata = null)
        {
            var historyEntry = new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = job.Status,
                ToStatus = job.Status,
                Source = source,
                // Not an error: the message rides in metadata so job.ErrorMessage stays clean.
                MetadataJson = JsonSerializer.Serialize(
                    new { message, details = metadata }, JsonOptions),
                ChangedAt = DateTime.UtcNow
            };

            unitOfWork.Repository<JobStatusHistory>().Add(historyEntry);
            await unitOfWork.Complete();

            logger.LogInformation("Job {JobId} event recorded | Source: {Source} | {Message}", job.Id, source, message);
        }


        /// <inheritdoc />
        public async Task RecordInitialJobStatusAsync(UserJob job, object? metadata = null)
        {
            var historyEntry = new JobStatusHistory
            {
                JobId = job.Id,
                FromStatus = null, // Initial status has no "from"
                ToStatus = job.Status,
                Source = StatusChangeSource.System,
                MetadataJson = metadata != null ? JsonSerializer.Serialize(metadata, JsonOptions) : null,
                ChangedAt = job.CreatedAt
            };

            unitOfWork.Repository<JobStatusHistory>().Add(historyEntry);
            await unitOfWork.Complete();

            logger.LogDebug("Recorded initial status {Status} for Job {JobId}", job.Status, job.Id);
        }


        /// <inheritdoc />
        public async Task<IReadOnlyList<JobTimelineEntryDto>> GetJobTimelineAsync(int jobId)
        {
            var historyEntries = await unitOfWork.Repository<JobStatusHistory>().ListAsync(
                criteria: h => h.JobId == jobId,
                orderBy: q => q.OrderBy(h => h.ChangedAt));

            return MapToJobTimelineEntries(historyEntries);
        }

        /// <inheritdoc />
        public async Task<PaginatedResult<JobTimelineEntryDto>> GetJobTimelinePaginatedAsync(int jobId, int pageNumber, int pageSize)
        {
            var totalCount = await unitOfWork.Repository<JobStatusHistory>().CountAsync(h => h.JobId == jobId);
            var historyEntries = await unitOfWork.Repository<JobStatusHistory>().ListAsync(
                criteria: h => h.JobId == jobId,
                orderBy: q => q.OrderBy(h => h.ChangedAt),
                skip: (pageNumber - 1) * pageSize,
                take: pageSize);

            return new PaginatedResult<JobTimelineEntryDto>
            {
                Items = MapToJobTimelineEntries(historyEntries),
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        private static List<JobTimelineEntryDto> MapToJobTimelineEntries(IReadOnlyList<JobStatusHistory> entries)
        {
            var result = new List<JobTimelineEntryDto>(entries.Count);
            DateTime? previousTime = null;

            foreach (var entry in entries)
            {
                result.Add(new JobTimelineEntryDto
                {
                    FromStatus = entry.FromStatus,
                    ToStatus = entry.ToStatus,
                    Source = entry.Source,
                    ErrorMessage = entry.ErrorMessage,
                    MetadataJson = entry.MetadataJson,
                    ChangedAt = entry.ChangedAt,
                    DurationFromPrevious = previousTime.HasValue 
                        ? entry.ChangedAt - previousTime.Value 
                        : null
                });

                previousTime = entry.ChangedAt;
            }

            return result;
        }

    }
}

