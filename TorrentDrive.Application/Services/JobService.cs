using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Common;
using TorrentDrive.Core.DTOs.Jobs;
using TorrentDrive.Core.DTOs.Torrents;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Entities.Torrents;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Extensions;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services
{
    public class JobService(
        IUnitOfWork unitOfWork,
        IJobStatusService jobStatusService,
        IServiceScopeFactory serviceScopeFactory,
        IJobHandlerFactory jobHandlerFactory,
        IJobCancellationSignal jobCancellationSignal,
        IUploadRoutingService uploadRoutingService,
        IStorageProfileHealthService storageHealthService,
        ILogger<JobService> logger) : IJobService
    {
        private const long OneGiB = 1L * 1024 * 1024 * 1024;
        private const long TenGiB = 10L * 1024 * 1024 * 1024;
        private const long FiftyGiB = 50L * 1024 * 1024 * 1024;

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".ts"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".iso"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".epub", ".mobi", ".doc", ".docx", ".txt", ".md"
        };

        private static readonly HashSet<string> SoftwareExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".dmg", ".deb", ".rpm", ".apk", ".appimage"
        };

        public async Task<JobCreationResult> CreateAndDispatchJobAsync(int torrentFileId, int userId, string[]? selectedFiles, int storageProfileId)
        {
            logger.LogInformation("Create and dispatch job requested | TorrentFileId: {TorrentFileId} | UserId: {UserId}", torrentFileId, userId);

            var torrentFile = await unitOfWork.Repository<RequestedFile>().GetByIdAsync(torrentFileId);
            if (torrentFile == null)
                throw new NotFoundException("TorrentNotFound", "Torrent file not found.");

            await CheckExistingActiveJobAsync(userId, torrentFileId);

            var storageProfile = await unitOfWork.Repository<UserStorageProfile>().GetByIdAsync(storageProfileId);
            if (storageProfile == null || storageProfile.UserId != userId || !storageProfile.IsActive)
                throw new ValidationException("InvalidStorageProfile", "Invalid or inactive storage profile.");

            await EnsureDestinationIsReachableAsync(storageProfile, userId);

            var defaultJobType = jobHandlerFactory.GetAllJobTypeHandlers().FirstOrDefault()?.JobType ?? JobType.Torrent;
            var job = new UserJob
            {
                UserId = userId,
                StorageProfileId = storageProfile.Id,
                Status = JobStatus.QUEUED,
                Type = defaultJobType,
                RequestFileId = torrentFileId,
                SelectedFilePaths = selectedFiles,
                TotalBytes = torrentFile.FileSize
            };

            unitOfWork.Repository<UserJob>().Add(job);
            await unitOfWork.Complete();

            logger.LogInformation("Job created | JobId: {JobId} | StorageProfileId: {StorageProfileId} | RequestFileId: {RequestFileId}",
                job.Id, storageProfile.Id, torrentFileId);

            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var hangfireJobId = await RetryDownloadPhaseAsync(job, backgroundJobClient);

            job.HangfireJobId = hangfireJobId;
            job.LastHeartbeat = DateTime.UtcNow;
            job.CurrentState = "Queued for download";
            await unitOfWork.Complete();

            await jobStatusService.RecordInitialJobStatusAsync(job, new
            {
                storageProfileId = storageProfile.Id,
                requestFileId = torrentFileId,
                selectedFilesCount = job.SelectedFilePaths?.Length ?? 0,
                hangfireJobId
            });

            logger.LogInformation("Job creation and dispatch completed successfully | JobId: {JobId} | UserId: {UserId}", job.Id, userId);

            return new JobCreationResult
            {
                JobId = job.Id,
                StorageProfileId = storageProfile.Id,
            };
        }

        /// <summary>
        /// Maximum torrents startable in one batch. Each item costs a health probe, an
        /// insert and a stream publish, so the request stays bounded.
        /// </summary>
        private const int MaxBatchSize = 20;

        public async Task<BatchJobCreationResultDto> CreateAndDispatchJobsAsync(int userId, CreateJobsRequestDto request)
        {
            if (request.Items == null || request.Items.Count == 0)
                throw new ValidationException("EmptyBatch", "No torrents were provided.");

            if (request.Items.Count > MaxBatchSize)
                throw new ValidationException("BatchTooLarge", $"You can start at most {MaxBatchSize} torrents at once.");

            logger.LogInformation("Batch job creation requested | UserId: {UserId} | Items: {Count}", userId, request.Items.Count);

            var outcomes = new List<JobCreationOutcomeDto>(request.Items.Count);

            foreach (var item in request.Items)
            {
                // Each item is dispatched on its own. A duplicate or an unreachable drive
                // fails that torrent only — the rest of the batch still starts.
                var storageProfileId = item.StorageProfileId ?? request.StorageProfileId;

                if (storageProfileId is not > 0)
                {
                    outcomes.Add(new JobCreationOutcomeDto
                    {
                        TorrentFileId = item.TorrentFileId,
                        Success = false,
                        ErrorCode = "InvalidStorageProfile",
                        ErrorMessage = "No storage destination was selected for this torrent."
                    });
                    continue;
                }

                try
                {
                    var result = await CreateAndDispatchJobAsync(
                        item.TorrentFileId, userId, item.SelectedFilePaths, storageProfileId.Value);

                    outcomes.Add(new JobCreationOutcomeDto
                    {
                        TorrentFileId = item.TorrentFileId,
                        JobId = result.JobId,
                        Success = true
                    });
                }
                catch (DomainException ex)
                {
                    logger.LogWarning(
                        "Batch item rejected | UserId: {UserId} | TorrentFileId: {TorrentFileId} | Code: {Code} | {Message}",
                        userId, item.TorrentFileId, ex.Code, ex.Message);

                    outcomes.Add(new JobCreationOutcomeDto
                    {
                        TorrentFileId = item.TorrentFileId,
                        Success = false,
                        ErrorCode = ex.Code,
                        ErrorMessage = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Batch item failed unexpectedly | UserId: {UserId} | TorrentFileId: {TorrentFileId}",
                        userId, item.TorrentFileId);

                    outcomes.Add(new JobCreationOutcomeDto
                    {
                        TorrentFileId = item.TorrentFileId,
                        Success = false,
                        ErrorCode = "JobCreationFailed",
                        ErrorMessage = "This torrent could not be started. Please try again."
                    });
                }
            }

            var succeeded = outcomes.Count(o => o.Success);

            logger.LogInformation("Batch job creation completed | UserId: {UserId} | Succeeded: {Succeeded} | Failed: {Failed}",
                userId, succeeded, outcomes.Count - succeeded);

            return new BatchJobCreationResultDto
            {
                Results = outcomes,
                SucceededCount = succeeded,
                FailedCount = outcomes.Count - succeeded
            };
        }

        public Task<PaginatedResult<JobDto>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null)
        {
            return GetUserJobsAsync(userId, new JobQueryParametersDto
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                Status = status
            });
        }

        public async Task<PaginatedResult<JobDto>> GetUserJobsAsync(int userId, JobQueryParametersDto query)
        {
            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            logger.LogDebug(
                "Get user jobs requested | UserId: {UserId} | Page: {PageNumber} | PageSize: {PageSize} | Phase: {Phase} | Status: {Status} | Search: {Search}",
                userId, pageNumber, pageSize, query.Phase, query.Status, query.EffectiveSearch);

            var allJobs = await unitOfWork.Repository<UserJob>().ListAsync(
                criteria: job => job.UserId == userId,
                orderBy: null,
                skip: null,
                take: null,
                job => job.StorageProfile,
                job => job.RequestFile);

            Dictionary<int, string>? profileNames = null;
            if (allJobs.Any(j => j.OriginalStorageProfileId.HasValue))
            {
                var userProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(p => p.UserId == userId);
                profileNames = userProfiles.ToDictionary(p => p.Id, p => p.ProfileName);
            }

            IEnumerable<UserJob> filtered = allJobs;

            // 1. Single status filter
            if (query.Status.HasValue)
            {
                var targetStatus = query.Status.Value;
                filtered = filtered.Where(j => j.Status == targetStatus);
            }

            // 2. Comma-separated statuses filter
            if (!string.IsNullOrWhiteSpace(query.Statuses))
            {
                var parsedStatuses = ParseStatusesList(query.Statuses);
                if (parsedStatuses.Count > 0)
                {
                    filtered = filtered.Where(j => parsedStatuses.Contains(j.Status));
                }
            }

            // 3. Phase filter
            if (!string.IsNullOrWhiteSpace(query.Phase))
            {
                filtered = ApplyPhaseFilter(filtered, query.Phase.Trim());
            }

            // 4. Provider filter
            if (!string.IsNullOrWhiteSpace(query.Provider))
            {
                var providerType = ParseProviderType(query.Provider);
                if (providerType.HasValue)
                {
                    filtered = filtered.Where(j => j.StorageProfile != null && j.StorageProfile.ProviderType == providerType.Value);
                }
            }

            // 5. StorageProfileId filter
            if (query.StorageProfileId.HasValue)
            {
                var targetProfileId = query.StorageProfileId.Value;
                filtered = filtered.Where(j => j.StorageProfileId == targetProfileId);
            }

            // 6. Media Category filter
            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                var categories = query.Category
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(c => c.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (categories.Count > 0)
                {
                    filtered = filtered.Where(j => categories.Contains(ComputeMediaCategory(j)));
                }
            }

            // 7. SizeBucket filter
            if (!string.IsNullOrWhiteSpace(query.SizeBucket))
            {
                var bucket = NormalizeSizeBucket(query.SizeBucket);
                if (bucket != null)
                {
                    filtered = filtered.Where(j => ComputeSizeBucket(GetEffectiveTotalBytes(j)) == bucket);
                }
            }

            // 8. DateRange filter
            if (!string.IsNullOrWhiteSpace(query.DateRange))
            {
                var cutoff = ParseDateRangeCutoff(query.DateRange);
                if (cutoff.HasValue)
                {
                    filtered = filtered.Where(j => j.CreatedAt >= cutoff.Value);
                }
            }

            // 9. Boolean facet filters
            if (query.IsPinned.HasValue)
            {
                var isPinned = query.IsPinned.Value;
                filtered = filtered.Where(j => (!j.AllowStorageFailover) == isPinned);
            }

            if (query.WasRerouted.HasValue)
            {
                var wasRerouted = query.WasRerouted.Value;
                filtered = filtered.Where(j => ComputeWasRerouted(j) == wasRerouted);
            }

            if (query.IsStalled.HasValue)
            {
                var isStalled = query.IsStalled.Value;
                filtered = filtered.Where(j => ComputeIsStalled(j) == isStalled);
            }

            if (query.IsSelective.HasValue)
            {
                var isSelective = query.IsSelective.Value;
                filtered = filtered.Where(j => ComputeIsSelective(j) == isSelective);
            }

            if (query.HasError.HasValue)
            {
                var hasError = query.HasError.Value;
                filtered = filtered.Where(j => (!string.IsNullOrEmpty(j.ErrorMessage)) == hasError);
            }

            // 10. Token-aware search & free-text filter
            var effectiveSearch = query.EffectiveSearch;
            if (!string.IsNullOrWhiteSpace(effectiveSearch))
            {
                filtered = ApplySearchTokensAndText(filtered, effectiveSearch);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // 11. Sorting
            var sorted = ApplySorting(filteredList, query.SortBy, query.SortDir);

            // 12. Pagination & Mapping
            var pagedItems = sorted
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(job => MapToJobDto(job, profileNames))
                .ToList();

            return new PaginatedResult<JobDto>
            {
                Items = pagedItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<JobDto> GetJobByIdAsync(int userId, int jobId, UserRole? userRole = null)
        {
            logger.LogDebug("Get job by ID requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            var job = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(
                job => job.Id == jobId && job.UserId == userId,
                job => job.StorageProfile,
                job => job.RequestFile);

            if (job == null)
            {
                logger.LogWarning("Job not found | JobId: {JobId} | UserId: {UserId}", jobId, userId);
                throw new NotFoundException("JobNotFound", "Job not found.");
            }

            var timeline = await jobStatusService.GetJobTimelineAsync(jobId);

            Dictionary<int, string>? profileNames = null;
            if (job.OriginalStorageProfileId.HasValue)
            {
                var originalProfile = await unitOfWork.Repository<UserStorageProfile>().GetByIdAsync(job.OriginalStorageProfileId.Value);
                if (originalProfile != null)
                {
                    profileNames = new Dictionary<int, string>
                    {
                        [originalProfile.Id] = originalProfile.ProfileName
                    };
                }
            }

            return MapToJobDto(job, profileNames, timeline.ToList());
        }

        public async Task<JobStatisticsDto> GetUserJobStatisticsAsync(int userId)
        {
            logger.LogDebug("Get user job statistics requested | UserId: {UserId}", userId);

            var allJobs = await unitOfWork.Repository<UserJob>().ListAsync(
                criteria: job => job.UserId == userId,
                orderBy: null,
                skip: null,
                take: null,
                job => job.StorageProfile,
                job => job.RequestFile);

            var statistics = new JobStatisticsDto
            {
                TotalJobs = allJobs.Count,
                ActiveJobs = allJobs.Count(j => j.Status.IsActive()),
                CompletedJobs = allJobs.Count(j => j.Status.IsCompleted()),
                FailedJobs = allJobs.Count(j => j.Status.IsFailed() && j.Status != JobStatus.CANCELLED),
                QueuedJobs = allJobs.Count(j => j.Status == JobStatus.QUEUED),
                DownloadingJobs = allJobs.Count(j => j.Status == JobStatus.DOWNLOADING),
                PendingUploadJobs = allJobs.Count(j => j.Status == JobStatus.PENDING_UPLOAD),
                UploadingJobs = allJobs.Count(j => j.Status == JobStatus.UPLOADING),
                RetryingJobs = allJobs.Count(j => j.Status.IsRetrying()),
                CancelledJobs = allJobs.Count(j => j.Status.IsCancelled()),
                PinnedJobs = allJobs.Count(j => !j.AllowStorageFailover),
                ReroutedJobs = allJobs.Count(ComputeWasRerouted),
                StalledJobs = allJobs.Count(ComputeIsStalled),
                SelectiveJobs = allJobs.Count(ComputeIsSelective),
                TotalBytesTransferred = allJobs.Sum(j =>
                    j.Status == JobStatus.COMPLETED
                        ? GetEffectiveTotalBytes(j)
                        : Math.Max(j.BytesDownloaded, j.BytesUploaded)),
                TotalDownloadSpeedBytesPerSecond = allJobs
                    .Where(j => j.Status == JobStatus.DOWNLOADING)
                    .Sum(j => j.DownloadSpeedBytesPerSecond),
                TotalUploadSpeedBytesPerSecond = allJobs
                    .Where(j => j.Status == JobStatus.UPLOADING)
                    .Sum(j => j.UploadSpeedBytesPerSecond)
            };

            statistics.StatusFilters = allJobs
                .GroupBy(j => j.Status)
                .Select(g => new JobStatusFilterDto { Status = g.Key, Count = g.Count() })
                .Where(f => f.Count > 0)
                .OrderByDescending(f => f.Count)
                .ToList();

            statistics.ProviderCounts = allJobs
                .Where(j => j.StorageProfile != null)
                .GroupBy(j => j.StorageProfile!.ProviderType.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            statistics.CategoryCounts = allJobs
                .GroupBy(ComputeMediaCategory)
                .ToDictionary(g => g.Key, g => g.Count());

            statistics.SizeBucketCounts = allJobs
                .GroupBy(j => ComputeSizeBucket(GetEffectiveTotalBytes(j)))
                .ToDictionary(g => g.Key, g => g.Count());

            statistics.ProfileCounts = allJobs
                .GroupBy(j => j.StorageProfileId)
                .Select(g =>
                {
                    var profile = g.FirstOrDefault(j => j.StorageProfile != null)?.StorageProfile;
                    return new StorageProfileFacetDto
                    {
                        StorageProfileId = g.Key,
                        ProfileName = profile?.ProfileName ?? $"Profile #{g.Key}",
                        ProviderType = profile?.ProviderType.ToString() ?? string.Empty,
                        Count = g.Count()
                    };
                })
                .OrderByDescending(p => p.Count)
                .ToList();

            return statistics;
        }

        public async Task<BatchJobActionResultDto> RetryFailedJobsAsync(int userId, BatchJobIdsRequestDto? request = null, UserRole? userRole = null)
        {
            List<int> targetJobIds;

            if (request?.JobIds is { Count: > 0 })
            {
                targetJobIds = request.JobIds.Distinct().ToList();
            }
            else
            {
                var userJobs = await unitOfWork.Repository<UserJob>().ListAsync(j => j.UserId == userId);
                targetJobIds = userJobs
                    .Where(j => j.Status.IsFailed() && j.Status != JobStatus.CANCELLED)
                    .Select(j => j.Id)
                    .ToList();
            }

            logger.LogInformation(
                "Batch retry failed jobs requested | UserId: {UserId} | Count: {Count} | TargetStorageProfileId: {TargetProfileId}",
                userId, targetJobIds.Count, request?.TargetStorageProfileId);

            return await ExecuteBatchJobActionAsync(
                targetJobIds,
                "retry",
                jobId => RetryJobAsync(jobId, userId, userRole, request?.TargetStorageProfileId));
        }

        public async Task<BatchJobActionResultDto> CancelJobsAsync(int userId, BatchJobIdsRequestDto request, UserRole? userRole = null)
        {
            if (request?.JobIds == null || request.JobIds.Count == 0)
            {
                return new BatchJobActionResultDto();
            }

            var targetJobIds = request.JobIds.Distinct().ToList();

            logger.LogInformation("Batch cancel jobs requested | UserId: {UserId} | Count: {Count}", userId, targetJobIds.Count);

            return await ExecuteBatchJobActionAsync(
                targetJobIds,
                "cancel",
                jobId => CancelJobAsync(jobId, userId, userRole));
        }

        private async Task<BatchJobActionResultDto> ExecuteBatchJobActionAsync(
            IReadOnlyCollection<int> targetJobIds,
            string actionName,
            Func<int, Task> action)
        {
            var result = new BatchJobActionResultDto
            {
                TotalRequested = targetJobIds.Count
            };

            foreach (var jobId in targetJobIds)
            {
                try
                {
                    await action(jobId);
                    result.SucceededJobIds.Add(jobId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Batch {ActionName} failed for job | JobId: {JobId}", actionName, jobId);
                    result.Errors.Add($"Job #{jobId}: {ex.Message}");
                }
            }

            result.SucceededCount = result.SucceededJobIds.Count;
            result.FailedCount = result.Errors.Count;

            return result;
        }

        public async Task<IReadOnlyList<UserJob>> GetActiveJobsByStorageProfileIdAsync(int storageProfileId)
        {
            logger.LogDebug("Get active jobs by storage profile requested | StorageProfileId: {StorageProfileId}", storageProfileId);

            var activeJobs = await unitOfWork.Repository<UserJob>().ListAsync(j =>
                j.StorageProfileId == storageProfileId &&
                (j.Status == JobStatus.QUEUED ||
                 j.Status == JobStatus.DOWNLOADING ||
                 j.Status == JobStatus.PENDING_UPLOAD ||
                 j.Status == JobStatus.UPLOADING ||
                 j.Status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                 j.Status == JobStatus.UPLOAD_RETRY));

            logger.LogDebug("Found {Count} active jobs for storage profile | StorageProfileId: {StorageProfileId}", activeJobs.Count, storageProfileId);
            return activeJobs;
        }

        public async Task RetryJobAsync(int jobId, int userId, UserRole? userRole = null, int? targetStorageProfileId = null)
        {
            logger.LogInformation("Retry job requested | JobId: {JobId} | UserId: {UserId} | Role: {Role} | TargetProfileId: {TargetProfileId}",
                jobId, userId, userRole, targetStorageProfileId);

            var job = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(
                j => j.Id == jobId,
                j => j.StorageProfile,
                j => j.RequestFile,
                j => j.User);
            ValidateJobExistsAndAuthorized(job, userId, userRole, "retry");

            // Status guards run first so a rejected retry never mutates the job.
            ValidateJobStatusForRetry(job!, userId);

            if (targetStorageProfileId.HasValue)
            {
                // Redirecting before the destination check is deliberate: a job that failed
                // *because* its profile went inactive is exactly the one worth redirecting.
                await uploadRoutingService.RouteToProfileAsync(
                    job!, targetStorageProfileId.Value, userId, allowFailover: job!.AllowStorageFailover);
            }
            else
            {
                // Manual retry means "try again from scratch": let failover reconsider every
                // profile, including ones that failed on an earlier attempt.
                await uploadRoutingService.ClearAttemptHistoryAsync(job!.Id);
            }

            ValidateRetryDestination(job!, userId);

            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            await CancelExistingHangfireJobsAsync(job!, backgroundJobClient, monitoringApi);

            var previousStatus = job!.Status;
            string? newHangfireJobId;
            JobStatus targetStatus;
            StatusChangeSource source = StatusChangeSource.User;

            if (IsUploadPhase(job.Status))
            {
                targetStatus = JobStatus.PENDING_UPLOAD;
                newHangfireJobId = await RetryUploadPhaseAsync(job, backgroundJobClient);
                logger.LogInformation("Retrying job from upload phase | JobId: {JobId} | PreviousStatus: {PreviousStatus} | HangfireJobId: {HangfireJobId}", job.Id, previousStatus, newHangfireJobId);
            }
            else
            {
                targetStatus = JobStatus.QUEUED;
                newHangfireJobId = await RetryDownloadPhaseAsync(job, backgroundJobClient);
                logger.LogInformation("Retrying job from download phase | JobId: {JobId} | PreviousStatus: {PreviousStatus} | HangfireJobId: {HangfireJobId}", job.Id, previousStatus, newHangfireJobId);
            }

            job.HangfireJobId = newHangfireJobId;
            job.HangfireUploadJobId = null;
            job.ErrorMessage = null;
            job.NextRetryAt = null;
            job.LastHeartbeat = DateTime.UtcNow;
            job.CurrentState = $"Manually retried by {userRole?.ToString() ?? "User"}";

            await jobStatusService.TransitionJobStatusAsync(
                job,
                targetStatus,
                source,
                metadata: new
                {
                    retriedFrom = previousStatus.ToString(),
                    retriedBy = userId,
                    retriedByRole = userRole?.ToString(),
                    retriedAt = DateTime.UtcNow,
                    hangfireJobId = newHangfireJobId
                });

            logger.LogInformation("Job retry completed successfully | JobId: {JobId} | TargetStatus: {TargetStatus} | UserId: {UserId}", job.Id, targetStatus, userId);
        }

        public async Task<JobDto> ChangeJobStorageProfileAsync(
            int jobId,
            int userId,
            int storageProfileId,
            bool allowFailover = true,
            UserRole? userRole = null)
        {
            logger.LogInformation(
                "Change job storage profile requested | JobId: {JobId} | UserId: {UserId} | TargetProfileId: {TargetProfileId} | AllowFailover: {AllowFailover}",
                jobId, userId, storageProfileId, allowFailover);

            var job = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(
                j => j.Id == jobId,
                j => j.StorageProfile,
                j => j.RequestFile);
            ValidateJobExistsAndAuthorized(job, userId, userRole, "change storage profile");
            ValidateJobForStorageChange(job!, userId);

            var previousProviderType = job!.StorageProfile?.ProviderType;

            var route = await uploadRoutingService.RouteToProfileAsync(job, storageProfileId, userId, allowFailover);

            // A job already waiting to upload has a queued Hangfire job bound to the old
            // destination. Replace it so the change takes effect without a manual retry.
            if (route.Rerouted && IsUploadPhase(job.Status) && !string.IsNullOrEmpty(job.DownloadPath))
            {
                await RedispatchUploadAsync(job, previousProviderType);
            }

            return await GetJobByIdAsync(userId, jobId, userRole);
        }

        // --- Mapping, Facet, Filtering & Sorting Helpers ---

        private static JobDto MapToJobDto(
            UserJob job,
            IReadOnlyDictionary<int, string>? profileNames = null,
            List<JobTimelineEntryDto>? timeline = null)
        {
            var totalFilesCount = job.RequestFile?.Files?.Length ?? 0;
            var selectedFilesCount = job.SelectedFilePaths is { Length: > 0 }
                ? job.SelectedFilePaths.Length
                : totalFilesCount;

            string? originalProfileName = null;
            if (job.OriginalStorageProfileId.HasValue && profileNames != null)
            {
                profileNames.TryGetValue(job.OriginalStorageProfileId.Value, out originalProfileName);
            }

            return new JobDto
            {
                Id = job.Id,
                StorageProfileId = job.StorageProfileId,
                StorageProfileName = job.StorageProfile?.ProfileName,
                StorageProviderType = job.StorageProfile?.ProviderType.ToString(),
                OriginalStorageProfileId = job.OriginalStorageProfileId,
                OriginalStorageProfileName = originalProfileName,
                AllowStorageFailover = job.AllowStorageFailover,
                FailoverAttempts = job.FailoverAttempts,
                LastRouteReason = job.LastRouteReason.ToString(),
                Status = job.Status,
                Type = job.Type.ToString(),
                RequestFileId = job.RequestFileId,
                RequestFileName = job.RequestFile?.FileName,
                InfoHash = job.RequestFile?.InfoHash,
                TotalFilesCount = totalFilesCount,
                SelectedFilesCount = selectedFilesCount,
                MediaCategory = ComputeMediaCategory(job),
                IsStalled = ComputeIsStalled(job),
                ErrorMessage = job.ErrorMessage,
                CurrentState = job.CurrentState,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                LastHeartbeat = job.LastHeartbeat,
                BytesDownloaded = job.BytesDownloaded,
                BytesUploaded = job.BytesUploaded,
                TotalBytes = GetEffectiveTotalBytes(job),
                DownloadSpeedBytesPerSecond = job.DownloadSpeedBytesPerSecond,
                UploadSpeedBytesPerSecond = job.UploadSpeedBytesPerSecond,
                SelectedFilePaths = job.SelectedFilePaths,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                Timeline = timeline ?? []
            };
        }

        private static long GetEffectiveTotalBytes(UserJob job)
        {
            return job.TotalBytes > 0 ? job.TotalBytes : (job.RequestFile?.FileSize ?? 0);
        }

        private static bool ComputeWasRerouted(UserJob job)
        {
            return job.OriginalStorageProfileId.HasValue && job.OriginalStorageProfileId != job.StorageProfileId;
        }

        private static bool ComputeIsSelective(UserJob job)
        {
            var totalFilesCount = job.RequestFile?.Files?.Length ?? 0;
            return job.SelectedFilePaths != null &&
                   job.SelectedFilePaths.Length > 0 &&
                   totalFilesCount > 0 &&
                   job.SelectedFilePaths.Length < totalFilesCount;
        }

        private static bool ComputeIsStalled(UserJob job)
        {
            var now = DateTime.UtcNow;
            if (job.Status == JobStatus.DOWNLOADING || job.Status == JobStatus.UPLOADING)
            {
                return (job.LastHeartbeat.HasValue && (now - job.LastHeartbeat.Value).TotalMinutes > 2) ||
                       (!job.LastHeartbeat.HasValue && job.StartedAt.HasValue && (now - job.StartedAt.Value).TotalMinutes > 2);
            }

            if (job.Status == JobStatus.QUEUED || job.Status == JobStatus.PENDING_UPLOAD)
            {
                return (now - (job.UpdatedAt ?? job.CreatedAt)).TotalMinutes > 5;
            }

            return false;
        }

        private static string ComputeMediaCategory(UserJob job)
        {
            var candidates = new List<string>();
            if (job.SelectedFilePaths is { Length: > 0 })
            {
                candidates.AddRange(job.SelectedFilePaths);
            }
            else if (job.RequestFile?.Files is { Length: > 0 })
            {
                candidates.AddRange(job.RequestFile.Files);
            }

            if (!string.IsNullOrWhiteSpace(job.RequestFile?.FileName))
            {
                candidates.Add(job.RequestFile.FileName);
            }

            var hasVideo = false;
            var hasAudio = false;
            var hasSoftware = false;
            var hasArchive = false;
            var hasDocument = false;

            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) continue;

                if (VideoExtensions.Contains(ext)) hasVideo = true;
                else if (AudioExtensions.Contains(ext)) hasAudio = true;
                else if (SoftwareExtensions.Contains(ext)) hasSoftware = true;
                else if (ArchiveExtensions.Contains(ext)) hasArchive = true;
                else if (DocumentExtensions.Contains(ext)) hasDocument = true;
            }

            if (hasVideo) return "video";
            if (hasAudio) return "audio";
            if (hasSoftware) return "software";
            if (hasArchive) return "archive";
            if (hasDocument) return "document";
            return "other";
        }

        private static string ComputeSizeBucket(long bytes)
        {
            if (bytes < OneGiB) return "small";
            if (bytes < TenGiB) return "medium";
            if (bytes < FiftyGiB) return "large";
            return "xl";
        }

        private static string? NormalizeSizeBucket(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Trim().ToLowerInvariant() switch
            {
                "small" or "<1gb" or "<1gib" or "xs" or "s" => "small",
                "medium" or "1-10gb" or "1-10gib" or "m" => "medium",
                "large" or "10-50gb" or "10-50gib" or "l" => "large",
                "xl" or ">50gb" or ">=50gb" or ">50gib" or ">=50gib" => "xl",
                _ => null
            };
        }

        private static StorageProviderType? ParseProviderType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Trim().ToLowerInvariant() switch
            {
                "googledrive" or "gdrive" or "google" or "google_drive" or "google-drive" => StorageProviderType.GoogleDrive,
                "s3" or "aws" or "r2" or "b2" or "minio" => StorageProviderType.S3,
                _ => null
            };
        }

        private static DateTime? ParseDateRangeCutoff(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var now = DateTime.UtcNow;
            return raw.Trim().ToLowerInvariant() switch
            {
                "today" or "24h" or "1d" => now.AddHours(-24),
                "7d" or "week" => now.AddDays(-7),
                "30d" or "month" => now.AddDays(-30),
                "90d" or "quarter" => now.AddDays(-90),
                _ => null
            };
        }

        private static HashSet<JobStatus> ParseStatusesList(string rawStatuses)
        {
            var result = new HashSet<JobStatus>();
            var tokens = rawStatuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (Enum.TryParse<JobStatus>(token, ignoreCase: true, out var status))
                {
                    result.Add(status);
                }
            }
            return result;
        }

        private static IEnumerable<UserJob> ApplyPhaseFilter(IEnumerable<UserJob> source, string phase)
        {
            return phase.ToLowerInvariant() switch
            {
                "all" => source,
                "active" => source.Where(j => j.Status.IsActive()),
                "downloading" => source.Where(j => j.Status == JobStatus.DOWNLOADING),
                "uploading" => source.Where(j =>
                    j.Status == JobStatus.UPLOADING ||
                    j.Status == JobStatus.PENDING_UPLOAD),
                "queued" => source.Where(j => j.Status == JobStatus.QUEUED),
                "pending_upload" or "pendingupload" => source.Where(j => j.Status == JobStatus.PENDING_UPLOAD),
                "retrying" => source.Where(j =>
                    j.Status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                    j.Status == JobStatus.UPLOAD_RETRY),
                "completed" => source.Where(j => j.Status == JobStatus.COMPLETED),
                "failed" => source.Where(j => j.Status.IsFailed() && j.Status != JobStatus.CANCELLED),
                "cancelled" or "canceled" => source.Where(j => j.Status == JobStatus.CANCELLED),
                _ => Enum.TryParse<JobStatus>(phase, ignoreCase: true, out var parsedStatus)
                    ? source.Where(j => j.Status == parsedStatus)
                    : source
            };
        }

        private static IEnumerable<UserJob> ApplySearchTokensAndText(IEnumerable<UserJob> source, string rawSearch)
        {
            var freeTextTerms = new List<string>();
            var matches = Regex.Matches(rawSearch, @"([^\s""]+:""[^""]+""|""[^""]+""|\S+)");

            foreach (Match match in matches)
            {
                var token = match.Value.Trim();
                if (string.IsNullOrEmpty(token)) continue;

                // #123 shorthand for job ID
                if (token.StartsWith('#') && int.TryParse(token[1..], out var hashJobId))
                {
                    var targetId = hashJobId;
                    source = source.Where(j => j.Id == targetId);
                    continue;
                }

                var colonIndex = token.IndexOf(':');
                if (colonIndex > 0 && colonIndex < token.Length - 1 && !token.StartsWith('"'))
                {
                    var key = token[..colonIndex].ToLowerInvariant();
                    var val = token[(colonIndex + 1)..].Trim('"');

                    switch (key)
                    {
                        case "id":
                        case "job":
                            if (int.TryParse(val.TrimStart('#'), out var parsedId))
                            {
                                var targetId = parsedId;
                                source = source.Where(j => j.Id == targetId);
                                continue;
                            }
                            break;

                        case "provider":
                            var provider = ParseProviderType(val);
                            if (provider.HasValue)
                            {
                                var p = provider.Value;
                                source = source.Where(j => j.StorageProfile != null && j.StorageProfile.ProviderType == p);
                                continue;
                            }
                            break;

                        case "phase":
                            source = ApplyPhaseFilter(source, val);
                            continue;

                        case "status":
                            if (Enum.TryParse<JobStatus>(val, ignoreCase: true, out var parsedStatus))
                            {
                                var st = parsedStatus;
                                source = source.Where(j => j.Status == st);
                                continue;
                            }
                            source = ApplyPhaseFilter(source, val);
                            continue;

                        case "is":
                        case "has":
                            var flag = val.ToLowerInvariant();
                            source = flag switch
                            {
                                "pinned" => source.Where(j => !j.AllowStorageFailover),
                                "rerouted" or "failover" => source.Where(ComputeWasRerouted),
                                "stalled" or "stuck" => source.Where(ComputeIsStalled),
                                "selective" or "partial" => source.Where(ComputeIsSelective),
                                "retrying" or "retry" => source.Where(j => j.Status.IsRetrying()),
                                "failed" or "error" => source.Where(j => j.Status.IsFailed() || !string.IsNullOrEmpty(j.ErrorMessage)),
                                "active" => source.Where(j => j.Status.IsActive()),
                                "completed" or "done" => source.Where(j => j.Status.IsCompleted()),
                                "cancelled" or "canceled" => source.Where(j => j.Status.IsCancelled()),
                                _ => source
                            };
                            continue;

                        case "type":
                        case "category":
                            var cat = val.ToLowerInvariant();
                            source = source.Where(j => string.Equals(ComputeMediaCategory(j), cat, StringComparison.OrdinalIgnoreCase));
                            continue;

                        case "ext":
                            var ext = val.StartsWith('.') ? val : "." + val;
                            source = source.Where(j => MatchesExtension(j, ext));
                            continue;

                        case "size":
                            var bucket = NormalizeSizeBucket(val);
                            if (bucket != null)
                            {
                                var b = bucket;
                                source = source.Where(j => ComputeSizeBucket(GetEffectiveTotalBytes(j)) == b);
                                continue;
                            }
                            break;
                    }
                }

                var unquoted = token.Trim('"');
                if (!string.IsNullOrWhiteSpace(unquoted))
                {
                    freeTextTerms.Add(unquoted);
                }
            }

            foreach (var term in freeTextTerms)
            {
                var searchTerm = term;
                source = source.Where(j => MatchesFreeText(j, searchTerm));
            }

            return source;
        }

        private static bool MatchesExtension(UserJob job, string ext)
        {
            if (!string.IsNullOrEmpty(job.RequestFile?.FileName) &&
                job.RequestFile.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (job.SelectedFilePaths != null &&
                job.SelectedFilePaths.Any(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (job.RequestFile?.Files != null &&
                job.RequestFile.Files.Any(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool MatchesFreeText(UserJob job, string term)
        {
            if (job.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(job.RequestFile?.FileName) &&
                job.RequestFile.FileName.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(job.RequestFile?.InfoHash) &&
                job.RequestFile.InfoHash.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(job.StorageProfile?.ProfileName) &&
                job.StorageProfile.ProfileName.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(job.ErrorMessage) &&
                job.ErrorMessage.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(job.CurrentState) &&
                job.CurrentState.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;

            if (job.SelectedFilePaths != null &&
                job.SelectedFilePaths.Any(p => p != null && p.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (job.RequestFile?.Files != null &&
                job.RequestFile.Files.Any(p => p != null && p.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static IEnumerable<UserJob> ApplySorting(List<UserJob> jobs, string? sortBy, string? sortDir)
        {
            var isAsc = string.Equals(sortDir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
            var normalizedSortBy = (sortBy ?? "createdAt").Trim().ToLowerInvariant();

            return normalizedSortBy switch
            {
                "size" or "totalbytes" => isAsc
                    ? jobs.OrderBy(GetEffectiveTotalBytes).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(GetEffectiveTotalBytes).ThenByDescending(j => j.Id),

                "progress" => isAsc
                    ? jobs.OrderBy(ComputeEffectiveProgress).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(ComputeEffectiveProgress).ThenByDescending(j => j.Id),

                "speed" => isAsc
                    ? jobs.OrderBy(ComputeEffectiveSpeed).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(ComputeEffectiveSpeed).ThenByDescending(j => j.Id),

                "name" or "filename" => isAsc
                    ? jobs.OrderBy(j => j.RequestFile?.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(j => j.RequestFile?.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenByDescending(j => j.Id),

                "completedat" => isAsc
                    ? jobs.OrderBy(j => j.CompletedAt ?? DateTime.MinValue).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(j => j.CompletedAt ?? DateTime.MinValue).ThenByDescending(j => j.Id),

                "updatedat" => isAsc
                    ? jobs.OrderBy(j => j.UpdatedAt ?? j.CreatedAt).ThenByDescending(j => j.Id)
                    : jobs.OrderByDescending(j => j.UpdatedAt ?? j.CreatedAt).ThenByDescending(j => j.Id),

                _ => isAsc
                    ? jobs.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
                    : jobs.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            };
        }

        private static double ComputeEffectiveProgress(UserJob job)
        {
            if (job.Status == JobStatus.COMPLETED)
                return 100.0;

            var total = GetEffectiveTotalBytes(job);
            if (total <= 0)
                return 0.0;

            if (job.Status == JobStatus.UPLOADING ||
                job.Status == JobStatus.UPLOAD_RETRY ||
                job.Status == JobStatus.UPLOAD_FAILED ||
                job.Status == JobStatus.GOOGLE_DRIVE_FAILED)
            {
                return Math.Clamp((job.BytesUploaded / (double)total) * 100.0, 0.0, 100.0);
            }

            return Math.Clamp((job.BytesDownloaded / (double)total) * 100.0, 0.0, 100.0);
        }

        private static double ComputeEffectiveSpeed(UserJob job)
        {
            if (job.Status == JobStatus.DOWNLOADING)
                return job.DownloadSpeedBytesPerSecond;
            if (job.Status == JobStatus.UPLOADING)
                return job.UploadSpeedBytesPerSecond;
            return Math.Max(job.DownloadSpeedBytesPerSecond, job.UploadSpeedBytesPerSecond);
        }

        /// <summary>
        /// Cancels the queued upload bound to the previous destination and enqueues a new
        /// one through the handler for the job's current provider.
        /// </summary>
        private async Task RedispatchUploadAsync(UserJob job, StorageProviderType? previousProviderType)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            await CancelExistingHangfireJobsAsync(job, backgroundJobClient, monitoringApi);

            // Release the upload lock held under the old provider so a re-run is not blocked.
            if (previousProviderType.HasValue)
            {
                var previousHandler = jobHandlerFactory.GetStorageProviderHandler(previousProviderType.Value);
                if (previousHandler != null)
                    await previousHandler.DeleteUploadLockAsync(job.Id);
            }

            var hangfireJobId = await RetryUploadPhaseAsync(job, backgroundJobClient);

            job.HangfireJobId = hangfireJobId;
            job.HangfireUploadJobId = null;
            job.ErrorMessage = null;
            job.NextRetryAt = null;
            job.LastHeartbeat = DateTime.UtcNow;
            job.CurrentState = $"Queued for upload to {job.StorageProfile?.ProfileName}";

            await jobStatusService.TransitionJobStatusAsync(
                job,
                JobStatus.PENDING_UPLOAD,
                StatusChangeSource.User,
                metadata: new
                {
                    storageProfileId = job.StorageProfileId,
                    storageProfileName = job.StorageProfile?.ProfileName,
                    redispatched = true,
                    hangfireJobId
                });

            logger.LogInformation("Upload re-dispatched after destination change | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                job.Id, hangfireJobId);
        }

        /// <summary>
        /// Fails fast at job creation when the chosen destination is unreachable and the
        /// user has no other profile of that provider for failover to land on.
        /// </summary>
        private async Task EnsureDestinationIsReachableAsync(UserStorageProfile profile, int userId)
        {
            var health = await storageHealthService.GetHealthAsync(profile);
            if (health.IsUsable) return;

            var alternatives = await unitOfWork.Repository<UserStorageProfile>().ListAsync(p =>
                p.UserId == userId &&
                p.IsActive &&
                p.Id != profile.Id &&
                p.ProviderType == profile.ProviderType &&
                !p.NeedsReauth);

            if (alternatives.Count == 0)
            {
                logger.LogWarning("Rejecting job creation: destination unusable with no alternative | ProfileId: {ProfileId} | Reason: {Reason}",
                    profile.Id, health.Reason);

                throw new BusinessRuleException(
                    "StorageUnhealthy",
                    health.Message ?? $"{profile.ProfileName} is not accepting uploads right now.");
            }

            // Another account can take the upload, so accept the job and let the upload
            // worker reroute it once the download finishes.
            logger.LogWarning(
                "Creating job against an unhealthy destination; failover will pick another profile | ProfileId: {ProfileId} | Reason: {Reason}",
                profile.Id, health.Reason);
        }

        /// <summary>
        /// Queues a download job holds its worker for. Kept in sync with the queue names
        /// on ITorrentDownloadJob and the worker's Hangfire server registration.
        /// </summary>
        private static readonly string[] DownloadQueues = ["torrents"];
        private static readonly string[] UploadQueues = ["googledrive", "s3"];

        public async Task<JobQueueStatusDto> GetQueueStatusAsync()
        {
            // Capacity comes from Hangfire (it owns the worker pools). Occupancy comes
            // from our own job rows rather than Hangfire's Processing count, which can
            // outlive the worker that owned it after a container restart and then
            // reports more in-flight jobs than there are workers to run them.
            var downloadCapacity = 0;
            var uploadCapacity = 0;

            try
            {
                var monitoringApi = JobStorage.Current?.GetMonitoringApi();
                foreach (var server in monitoringApi?.Servers() ?? [])
                {
                    var queues = server.Queues ?? [];
                    if (queues.Any(q => DownloadQueues.Contains(q, StringComparer.OrdinalIgnoreCase)))
                        downloadCapacity += server.WorkersCount;

                    if (queues.Any(q => UploadQueues.Contains(q, StringComparer.OrdinalIgnoreCase)))
                        uploadCapacity += server.WorkersCount;
                }
            }
            catch (Exception ex)
            {
                // Capacity is advisory. Zero means "unknown" to callers, which is better
                // than failing the request the UI needs to explain a waiting job.
                logger.LogWarning(ex, "Could not read Hangfire server capacity");
            }

            var activeDownloads = await unitOfWork.Repository<UserJob>().CountAsync(
                j => j.Status == JobStatus.DOWNLOADING);

            var queuedDownloads = await unitOfWork.Repository<UserJob>().CountAsync(
                j => j.Status == JobStatus.QUEUED);

            var activeUploads = await unitOfWork.Repository<UserJob>().CountAsync(
                j => j.Status == JobStatus.UPLOADING);

            return new JobQueueStatusDto
            {
                DownloadCapacity = downloadCapacity,
                ActiveDownloads = activeDownloads,
                QueuedDownloads = queuedDownloads,
                UploadCapacity = uploadCapacity,
                ActiveUploads = activeUploads
            };
        }

        public async Task ForceStartJobAsync(int jobId, int userId, UserRole? userRole = null)
        {
            logger.LogInformation("Force start requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            var job = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(
                j => j.Id == jobId,
                j => j.StorageProfile,
                j => j.RequestFile,
                j => j.User);
            ValidateJobExistsAndAuthorized(job, userId, userRole, "force start");
            ValidateJobForForceStart(job!, userId);

            var isUploadPhase = IsUploadPhase(job!.Status);

            if (isUploadPhase && job.StorageProfile == null)
                throw new BusinessRuleException("StorageInactive", "This job has no storage profile assigned.");

            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            // Drop any half-dispatched Hangfire job first. Enqueueing a second downloader
            // against the same directory would have two torrent engines writing the same
            // files, which is worse than staying stuck.
            await CancelExistingHangfireJobsAsync(job, backgroundJobClient, monitoringApi);

            var previousStatus = job.Status;

            // Enqueue directly rather than re-publishing to the Redis stream: the stream
            // hand-off is the step that failed, so going around it is the point.
            var hangfireJobId = isUploadPhase
                ? await RetryUploadPhaseAsync(job, backgroundJobClient)
                : await RetryDownloadPhaseAsync(job, backgroundJobClient);

            job.HangfireJobId = hangfireJobId;
            job.HangfireUploadJobId = null;
            job.ErrorMessage = null;
            job.NextRetryAt = null;
            job.LastHeartbeat = DateTime.UtcNow;
            job.CurrentState = isUploadPhase ? "Upload re-dispatched by user" : "Download re-dispatched by user";

            // The status does not change — the job was already QUEUED or PENDING_UPLOAD and
            // still is. Record the event so the timeline shows the manual intervention.
            await jobStatusService.RecordJobEventAsync(
                job,
                isUploadPhase
                    ? "Upload re-dispatched manually after the queue hand-off was lost."
                    : "Download re-dispatched manually after the queue hand-off was lost.",
                StatusChangeSource.User,
                new
                {
                    forceStarted = true,
                    status = previousStatus.ToString(),
                    requestedBy = userId,
                    hangfireJobId
                });

            await unitOfWork.Complete();

            logger.LogInformation("Force start dispatched | JobId: {JobId} | Status: {Status} | HangfireJobId: {HangfireJobId}",
                job.Id, previousStatus, hangfireJobId);
        }

        public async Task CancelJobAsync(int jobId, int userId, UserRole? userRole = null)
        {
            logger.LogInformation("Cancel job requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            var job = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(
                j => j.Id == jobId,
                j => j.StorageProfile);
            ValidateJobExistsAndAuthorized(job, userId, userRole, "cancel");

            ValidateJobForCancel(job!, userId, userRole);

            // Signal the worker process via Redis BEFORE touching Hangfire so the running
            // worker's monitoring loop detects cancellation as quickly as possible.
            // (BackgroundJob.Delete only prevents a queued job from starting; it does NOT
            // cancel the CancellationToken of an already-executing Hangfire job.)
            await jobCancellationSignal.SignalAsync(job!.Id);

            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            await CancelExistingHangfireJobsAsync(job!, backgroundJobClient, monitoringApi);

            var cancellationHandler = jobHandlerFactory.GetCancellationHandler(job!.Type);
            if (cancellationHandler != null)
            {
                await cancellationHandler.CancelJobAsync(job);
                logger.LogDebug("Executed cancellation handler for job type {JobType} | JobId: {JobId}", job.Type, job.Id);
            }

            if (job.StorageProfile != null)
            {
                var storageHandler = jobHandlerFactory.GetStorageProviderHandler(job.StorageProfile.ProviderType);
                if (storageHandler != null)
                {
                    await storageHandler.DeleteUploadLockAsync(job.Id);
                    logger.LogDebug("Deleted upload lock for provider {Provider} | JobId: {JobId}", job.StorageProfile.ProviderType, job.Id);
                }
            }

            job.CompletedAt = DateTime.UtcNow;
            job.HangfireJobId = null;
            job.HangfireUploadJobId = null;
            job.CurrentState = "Cancelled by User";

            StatusChangeSource source = StatusChangeSource.User;

            await jobStatusService.TransitionJobStatusAsync(
                job,
                JobStatus.CANCELLED,
                source,
                metadata: new
                {
                    cancelledBy = userId,
                    cancelledByRole = userRole?.ToString(),
                    cancelledAt = DateTime.UtcNow
                });

            logger.LogInformation("Job cancellation completed successfully | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
        }

        // --- Guard Helpers ---

        private void ValidateJobExistsAndAuthorized(UserJob? job, int userId, UserRole? userRole, string operation)
        {
            if (job == null)
            {
                logger.LogWarning("Job not found for {Operation} | UserId: {UserId}", operation, userId);
                throw new NotFoundException("JobNotFound", "Job not found.");
            }

            if (job.UserId != userId)
            {
                logger.LogWarning("Unauthorized {Operation} attempt | JobId: {JobId} | JobOwner: {OwnerId} | RequestingUser: {UserId}", operation, job.Id, job.UserId, userId);
                throw new ForbiddenException("Forbidden", "You are not authorized to perform this operation on this job.");
            }
        }

        private void ValidateJobStatusForRetry(UserJob job, int userId)
        {
            if (job.Status == JobStatus.COMPLETED)
            {
                logger.LogWarning("Attempt to retry completed job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException("JobCompleted", "Cannot retry a completed job.");
            }

            if (job.Status == JobStatus.CANCELLED)
            {
                logger.LogWarning("Attempt to retry cancelled job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException("JobCancelled", "Cannot retry a cancelled job.");
            }

            if (job.Status.IsActive())
            {
                logger.LogWarning("Attempt to retry active job | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                throw new BusinessRuleException("JobActive", $"Job is currently {job.Status}. Wait for it to complete or fail before retrying.");
            }
        }

        /// <summary>
        /// Runs after any requested redirect, so it checks the destination the retry will
        /// actually use rather than the one that failed.
        /// </summary>
        private void ValidateRetryDestination(UserJob job, int userId)
        {
            if (job.StorageProfile == null || !job.StorageProfile.IsActive)
            {
                logger.LogWarning("Attempt to retry job with inactive storage profile | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException(
                    "StorageInactive",
                    "The storage profile for this job is no longer active. Retry it against another drive.");
            }
        }

        /// <summary>
        /// Force start is only for the two states where a job is waiting on a queue
        /// hand-off. Everything else either has Hangfire actively working it, has a
        /// scheduled retry pending, or is terminal — re-dispatching those would duplicate
        /// work rather than unstick anything.
        /// </summary>
        private void ValidateJobForForceStart(UserJob job, int userId)
        {
            if (job.Status is JobStatus.QUEUED or JobStatus.PENDING_UPLOAD) return;

            logger.LogWarning("Attempt to force start a job that is not waiting to be picked up | JobId: {JobId} | Status: {Status} | UserId: {UserId}",
                job.Id, job.Status, userId);

            if (job.Status.IsRetrying())
            {
                throw new BusinessRuleException(
                    "JobRetrying",
                    "This job already has a retry scheduled. Wait for it, or retry it manually.");
            }

            if (job.Status.IsActive())
            {
                throw new BusinessRuleException(
                    "JobActive",
                    $"This job is already {job.Status} and does not need to be started.");
            }

            throw new BusinessRuleException(
                "JobNotStartable",
                $"A {job.Status} job cannot be force started. Use retry instead.");
        }

        /// <summary>
        /// A destination can be changed any time before bytes are moving. While an upload
        /// is actually running the worker holds a lock and a token for the old profile, so
        /// the change has to go through a retry instead.
        /// </summary>
        private void ValidateJobForStorageChange(UserJob job, int userId)
        {
            if (job.Status == JobStatus.COMPLETED)
            {
                logger.LogWarning("Attempt to reroute completed job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException("JobCompleted", "Cannot change the destination of a completed job.");
            }

            if (job.Status == JobStatus.CANCELLED)
            {
                logger.LogWarning("Attempt to reroute cancelled job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException("JobCancelled", "Cannot change the destination of a cancelled job.");
            }

            if (job.Status == JobStatus.UPLOADING)
            {
                logger.LogWarning("Attempt to reroute job during upload | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException(
                    "JobUploading",
                    "This job is uploading right now. Cancel or wait for it to fail, then retry it against another drive.");
            }
        }

        private void ValidateJobForCancel(UserJob job, int userId, UserRole? userRole)
        {
            if (job.Status == JobStatus.COMPLETED)
            {
                logger.LogWarning("Attempt to cancel completed job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new BusinessRuleException("JobCompleted", "Cannot cancel a completed job.");
            }

            if (job.Status == JobStatus.CANCELLED)
            {
                logger.LogWarning("Attempt to cancel already cancelled job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                throw new ConflictException("JobAlreadyCancelled", "Job is already cancelled.");
            }

            if (IsUploadPhase(job.Status))
            {
                logger.LogWarning("Attempt to cancel job during upload phase | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                throw new BusinessRuleException("JobInUploadPhase", "Cannot cancel a job during upload phase. Please wait for the upload to complete.");
            }

            if (!job.Status.IsActive() && !job.Status.IsFailed())
            {
                logger.LogWarning("Attempt to cancel non-cancellable job | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                throw new BusinessRuleException("JobNotCancellable", $"Job status {job.Status} cannot be cancelled.");
            }
        }

        private async Task CheckExistingActiveJobAsync(int userId, int requestFileId)
        {
            var failedStatuses = new HashSet<JobStatus>
            {
                JobStatus.COMPLETED,
                JobStatus.FAILED,
                JobStatus.CANCELLED
            };

            foreach (var handler in jobHandlerFactory.GetAllJobTypeHandlers())
                foreach (var status in handler.GetFailedStatuses())
                    failedStatuses.Add(status);

            var existingJob = await unitOfWork.Repository<UserJob>().FirstOrDefaultAsync(j =>
                j.UserId == userId &&
                j.RequestFileId == requestFileId &&
                !failedStatuses.Contains(j.Status));

            if (existingJob != null)
            {
                if (existingJob.Status.IsRetrying())
                {
                    var nextRetry = existingJob.NextRetryAt.HasValue
                        ? $" Next retry: {existingJob.NextRetryAt.Value:u}" : "";
                    logger.LogWarning("Attempt to create job while existing job is retrying | ExistingJobId: {ExistingJobId} | RequestFileId: {RequestFileId} | UserId: {UserId}", existingJob.Id, requestFileId, userId);
                    throw new BusinessRuleException("JobRetrying", $"Job {existingJob.Id} is currently retrying.{nextRetry}");
                }

                logger.LogWarning("Attempt to create job while active job exists | ExistingJobId: {ExistingJobId} | Status: {Status} | RequestFileId: {RequestFileId} | UserId: {UserId}", existingJob.Id, existingJob.Status, requestFileId, userId);
                throw new ConflictException("JobAlreadyExists", $"Active job exists. ID: {existingJob.Id}, Status: {existingJob.Status}");
            }
        }

        private Task CancelExistingHangfireJobsAsync(UserJob job, Hangfire.IBackgroundJobClient backgroundJobClient, Hangfire.Storage.IMonitoringApi? monitoringApi)
        {
            try
            {
                if (!string.IsNullOrEmpty(job.HangfireJobId))
                {
                    if (monitoringApi != null)
                    {
                        var downloadJob = monitoringApi.JobDetails(job.HangfireJobId);
                        if (downloadJob != null && downloadJob.History?.FirstOrDefault()?.StateName != "Succeeded")
                        {
                            BackgroundJob.Delete(job.HangfireJobId);
                            logger.LogDebug("Cancelled Hangfire download job | JobId: {JobId} | HangfireJobId: {HangfireJobId}", job.Id, job.HangfireJobId);
                        }
                    }
                    else
                    {
                        BackgroundJob.Delete(job.HangfireJobId);
                        logger.LogDebug("Cancelled Hangfire download job (no monitoring API) | JobId: {JobId} | HangfireJobId: {HangfireJobId}", job.Id, job.HangfireJobId);
                    }
                }

                if (!string.IsNullOrEmpty(job.HangfireUploadJobId))
                {
                    if (monitoringApi != null)
                    {
                        var uploadJob = monitoringApi.JobDetails(job.HangfireUploadJobId);
                        if (uploadJob != null && uploadJob.History?.FirstOrDefault()?.StateName != "Succeeded")
                        {
                            BackgroundJob.Delete(job.HangfireUploadJobId);
                            logger.LogDebug("Cancelled Hangfire upload job | JobId: {JobId} | HangfireUploadJobId: {HangfireUploadJobId}", job.Id, job.HangfireUploadJobId);
                        }
                    }
                    else
                    {
                        BackgroundJob.Delete(job.HangfireUploadJobId);
                        logger.LogDebug("Cancelled Hangfire upload job (no monitoring API) | JobId: {JobId} | HangfireUploadJobId: {HangfireUploadJobId}", job.Id, job.HangfireUploadJobId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cancelling Hangfire jobs | JobId: {JobId}", job.Id);
            }

            return Task.CompletedTask;
        }

        private Task<string> RetryUploadPhaseAsync(UserJob job, Hangfire.IBackgroundJobClient client)
        {
            if (job.StorageProfile == null)
                throw new InvalidOperationException($"Job {job.Id} has no storage profile assigned");

            var storageHandler = jobHandlerFactory.GetStorageProviderHandler(job.StorageProfile.ProviderType);
            if (storageHandler == null)
                throw new NotSupportedException($"No upload handler for provider: {job.StorageProfile.ProviderType}");

            return Task.FromResult(storageHandler.EnqueueUploadJob(job.Id, client));
        }

        private Task<string> RetryDownloadPhaseAsync(UserJob job, Hangfire.IBackgroundJobClient client)
        {
            if (job.Status.IsFailed())
            {
                job.BytesDownloaded = 0;
                job.DownloadPath = null;
            }

            var jobTypeHandler = jobHandlerFactory.GetJobTypeHandler(job.Type);
            if (jobTypeHandler == null)
                throw new NotSupportedException($"No download handler for job type: {job.Type}");

            return Task.FromResult(jobTypeHandler.EnqueueDownloadJob(job.Id, client));
        }

        private bool IsUploadPhase(JobStatus status)
        {
            var jobTypeHandlers = jobHandlerFactory.GetAllJobTypeHandlers();
            foreach (var handler in jobTypeHandlers)
                if (handler.IsUploadPhaseStatus(status))
                    return true;
            return false;
        }
    }
}
