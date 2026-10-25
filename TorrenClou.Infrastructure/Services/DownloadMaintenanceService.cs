using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrenClou.Core.DTOs.Maintenance;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Infrastructure.Services
{
    /// <summary>
    /// Reports what the downloads volume holds and reclaims the directories of finished
    /// jobs. Only COMPLETED and CANCELLED jobs are ever deleted — anything still running,
    /// retrying, or failed keeps its files, and a directory with no matching job row is
    /// counted but never touched.
    /// </summary>
    public class DownloadMaintenanceService(
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<DownloadMaintenanceService> logger) : IDownloadMaintenanceService
    {
        private const string DefaultDownloadPath = "/app/downloads";
        private const string LogPrefix = "[PURGE]";

        /// <summary>Job states whose local files are dead weight.</summary>
        private static readonly HashSet<JobStatus> PurgeableStatuses =
        [
            JobStatus.COMPLETED,
            JobStatus.CANCELLED
        ];

        public async Task<DownloadStoragePreviewDto> GetPreviewAsync(int userId, CancellationToken cancellationToken = default)
        {
            var basePath = ResolveBasePath();

            if (!Directory.Exists(basePath))
            {
                logger.LogWarning("{LogPrefix} Downloads directory not found | Path: {Path}", LogPrefix, basePath);
                return new DownloadStoragePreviewDto
                {
                    Warning = $"The downloads directory ({basePath}) is not accessible from the API."
                };
            }

            var (purgeable, retained, orphaned) = await ClassifyAsync(userId, basePath, cancellationToken);

            return new DownloadStoragePreviewDto
            {
                Purgeable = [.. purgeable.OrderByDescending(d => d.SizeBytes)],
                PurgeableCount = purgeable.Count,
                PurgeableBytes = purgeable.Sum(d => d.SizeBytes),
                RetainedCount = retained.Count,
                RetainedBytes = retained.Sum(d => d.SizeBytes),
                OrphanedCount = orphaned.Count,
                OrphanedBytes = orphaned.Sum(d => d.SizeBytes),
                TotalBytes =
                    purgeable.Sum(d => d.SizeBytes) +
                    retained.Sum(d => d.SizeBytes) +
                    orphaned.Sum(d => d.SizeBytes)
            };
        }

        public async Task<PurgeDownloadsResultDto> PurgeAsync(int userId, CancellationToken cancellationToken = default)
        {
            var basePath = ResolveBasePath();

            if (!Directory.Exists(basePath))
            {
                logger.LogWarning("{LogPrefix} Downloads directory not found, nothing to purge | Path: {Path}", LogPrefix, basePath);
                return new PurgeDownloadsResultDto();
            }

            var (purgeable, _, _) = await ClassifyAsync(userId, basePath, cancellationToken);

            var deleted = 0;
            var freed = 0L;
            var failures = new List<string>();

            foreach (var entry in purgeable)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var target = Path.Combine(basePath, entry.DirectoryName);

                // Re-run the containment check against the resolved path rather than
                // trusting the name that came out of the scan.
                if (!IsInsideBase(target, basePath, out var fullTarget))
                {
                    logger.LogError("{LogPrefix} Refusing to delete a path outside the download root | Path: {Path} | Root: {Root}",
                        LogPrefix, target, basePath);
                    failures.Add(entry.DirectoryName);
                    continue;
                }

                try
                {
                    Directory.Delete(fullTarget, recursive: true);
                    deleted++;
                    freed += entry.SizeBytes;

                    logger.LogInformation("{LogPrefix} Removed | JobId: {JobId} | Path: {Path} | Freed: {FreedMB:F2} MB",
                        LogPrefix, entry.JobId, fullTarget, entry.SizeBytes / (1024.0 * 1024.0));
                }
                catch (Exception ex)
                {
                    // One locked directory must not abort the sweep.
                    logger.LogWarning(ex, "{LogPrefix} Failed to remove | JobId: {JobId} | Path: {Path}",
                        LogPrefix, entry.JobId, fullTarget);
                    failures.Add(entry.DirectoryName);
                }
            }

            logger.LogInformation("{LogPrefix} Purge complete | UserId: {UserId} | Deleted: {Deleted} | Freed: {FreedMB:F2} MB | Failed: {Failed}",
                LogPrefix, userId, deleted, freed / (1024.0 * 1024.0), failures.Count);

            return new PurgeDownloadsResultDto
            {
                DeletedCount = deleted,
                FreedBytes = freed,
                FailedCount = failures.Count,
                Failures = failures
            };
        }

        // --- Classification ---

        /// <summary>
        /// Walks the download root once and buckets each directory. Directories owned by
        /// another user's job are dropped entirely rather than reported as orphans.
        /// </summary>
        private async Task<(List<DownloadDirectoryDto> Purgeable, List<DownloadDirectoryDto> Retained, List<DownloadDirectoryDto> Orphaned)>
            ClassifyAsync(int userId, string basePath, CancellationToken cancellationToken)
        {
            var purgeable = new List<DownloadDirectoryDto>();
            var retained = new List<DownloadDirectoryDto>();
            var orphaned = new List<DownloadDirectoryDto>();

            var directories = Directory.EnumerateDirectories(basePath).ToList();
            if (directories.Count == 0) return (purgeable, retained, orphaned);

            // Directory names are job ids (see TorrentDownloadJob.InitializeDownloadPath).
            var jobIds = directories
                .Select(d => int.TryParse(Path.GetFileName(d), out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var jobsById = new Dictionary<int, UserJob>();
            var knownJobIds = new HashSet<int>();

            if (jobIds.Count > 0)
            {
                // Load without the user filter first: a job that exists but belongs to
                // someone else must not be mistaken for an orphan.
                var matchingJobs = await unitOfWork.Repository<UserJob>().ListAsync(
                    criteria: j => jobIds.Contains(j.Id),
                    includes: j => j.RequestFile);

                foreach (var job in matchingJobs)
                {
                    knownJobIds.Add(job.Id);
                    if (job.UserId == userId) jobsById[job.Id] = job;
                }
            }

            foreach (var directory in directories)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var name = Path.GetFileName(directory);
                var parsed = int.TryParse(name, out var jobId);

                if (parsed && knownJobIds.Contains(jobId) && !jobsById.ContainsKey(jobId))
                {
                    // Belongs to another user — not ours to count or delete.
                    continue;
                }

                var size = GetDirectorySize(directory);

                if (parsed && jobsById.TryGetValue(jobId, out var job))
                {
                    var dto = new DownloadDirectoryDto
                    {
                        JobId = job.Id,
                        DirectoryName = name,
                        SizeBytes = size,
                        JobStatus = job.Status.ToString(),
                        TorrentName = job.RequestFile?.FileName,
                        CompletedAt = job.CompletedAt
                    };

                    if (PurgeableStatuses.Contains(job.Status)) purgeable.Add(dto);
                    else retained.Add(dto);
                }
                else
                {
                    orphaned.Add(new DownloadDirectoryDto
                    {
                        JobId = parsed ? jobId : null,
                        DirectoryName = name,
                        SizeBytes = size
                    });
                }
            }

            return (purgeable, retained, orphaned);
        }

        private string ResolveBasePath()
        {
            var configured = configuration["TORRENT_DOWNLOAD_PATH"] ?? DefaultDownloadPath;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        }

        /// <summary>
        /// True when <paramref name="candidate"/> resolves to a directory strictly inside
        /// the base path. Rejects the root itself, traversal, and prefix-collision
        /// siblings such as /app/downloads_evil.
        /// </summary>
        private static bool IsInsideBase(string candidate, string basePath, out string fullTarget)
        {
            fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var fullBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));

            if (string.Equals(fullTarget, fullBase, StringComparison.Ordinal)) return false;

            return fullTarget.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0L;
            }
        }
    }
}
