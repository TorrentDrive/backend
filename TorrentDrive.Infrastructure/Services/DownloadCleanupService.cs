using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services
{
    /// <summary>
    /// Deletes a job's download directory after its contents have been uploaded,
    /// so the shared downloads volume does not grow without bound.
    /// </summary>
    public class DownloadCleanupService(
        IConfiguration configuration,
        IUserSettingsService userSettingsService,
        ILogger<DownloadCleanupService> logger) : IDownloadCleanupService
    {
        private const string DefaultDownloadPath = "/app/downloads";
        private const string LogPrefix = "[CLEANUP]";

        public async Task<long> CleanupAfterUploadAsync(UserJob job, CancellationToken cancellationToken = default)
        {
            try
            {
                // Owned by the job's user and edited from the Settings tab, so the choice
                // survives redeploys instead of living in a worker environment variable.
                var settings = await userSettingsService.GetOrCreateAsync(job.UserId);
                if (!settings.DeleteAfterUpload)
                {
                    logger.LogDebug("{LogPrefix} Disabled by user settings | JobId: {JobId} | UserId: {UserId}",
                        LogPrefix, job.Id, job.UserId);
                    return 0L;
                }

                if (string.IsNullOrWhiteSpace(job.DownloadPath))
                {
                    logger.LogDebug("{LogPrefix} No download path recorded | JobId: {JobId}", LogPrefix, job.Id);
                    return 0L;
                }

                var basePath = configuration["TORRENT_DOWNLOAD_PATH"] ?? DefaultDownloadPath;

                // Resolve both sides before comparing. DownloadPath is persisted per job and
                // could be stale, relative, or from an older layout, so it is never trusted
                // as a delete target until it is proven to sit strictly inside the base path.
                var fullBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
                var fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.DownloadPath));

                if (string.Equals(fullTarget, fullBase, StringComparison.Ordinal))
                {
                    logger.LogError("{LogPrefix} Refusing to delete the download root | JobId: {JobId} | Path: {Path}",
                        LogPrefix, job.Id, fullTarget);
                    return 0L;
                }

                if (!fullTarget.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    logger.LogError("{LogPrefix} Refusing to delete a path outside the download root | JobId: {JobId} | Path: {Path} | Root: {Root}",
                        LogPrefix, job.Id, fullTarget, fullBase);
                    return 0L;
                }

                if (!Directory.Exists(fullTarget))
                {
                    logger.LogDebug("{LogPrefix} Nothing to remove | JobId: {JobId} | Path: {Path}", LogPrefix, job.Id, fullTarget);
                    return 0L;
                }

                var freedBytes = GetDirectorySize(fullTarget);
                Directory.Delete(fullTarget, recursive: true);

                logger.LogInformation("{LogPrefix} Removed download directory | JobId: {JobId} | Path: {Path} | Freed: {FreedMB:F2} MB",
                    LogPrefix, job.Id, fullTarget, freedBytes / (1024.0 * 1024.0));

                return freedBytes;
            }
            catch (Exception ex)
            {
                // The upload already succeeded; failing the job over a cleanup problem
                // would be worse than leaving the files behind for the operator to reap.
                logger.LogWarning(ex, "{LogPrefix} Failed to remove download directory | JobId: {JobId} | Path: {Path}",
                    LogPrefix, job.Id, job.DownloadPath);
                return 0L;
            }
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
