using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Handlers
{
    /// <summary>
    /// Handles torrent job cancellation including cleanup of resources.
    /// Note: The actual torrent manager stopping is handled by Hangfire's cancellation token
    /// mechanism and the TorrentDownloadJob's finally block. This handler focuses on
    /// resource cleanup that may be needed after the job is cancelled.
    /// </summary>
    public class TorrentCancellationHandler : IJobCancellationHandler
    {
        private readonly ILogger<TorrentCancellationHandler> _logger;

        public TorrentCancellationHandler(ILogger<TorrentCancellationHandler> logger)
        {
            _logger = logger;
        }

        public JobType JobType => JobType.Torrent;

        public async Task CancelJobAsync(UserJob job, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Cancelling torrent job | JobId: {JobId} | Status: {Status} | DownloadPath: {DownloadPath}", 
                job.Id, job.Status, job.DownloadPath);

            // The actual torrent manager stopping is handled by:
            // 1. Hangfire job cancellation (via BackgroundJob.Delete) which triggers CancellationToken
            // 2. The finally block in TorrentDownloadJob.ExecuteCoreAsync which calls manager.StopAsync()
            // 3. OnJobCancelledAsync which saves the engine state

            // Clean up any resources that might be left behind
            await CleanupResourcesAsync(job, cancellationToken);

            _logger.LogInformation("Torrent job cancellation completed | JobId: {JobId}", job.Id);
        }

        public Task CleanupResourcesAsync(UserJob job, CancellationToken cancellationToken = default)
        {
            try
            {
                // Only clean up the download directory if explicitly requested (e.g., on permanent cancellation)
                // For retry scenarios, we want to keep the partial download for resume
                
                // Clean up FastResume data and other temp files if they exist
                if (!string.IsNullOrEmpty(job.DownloadPath) && Directory.Exists(job.DownloadPath))
                {
                    var dhtCacheFile = Path.Combine(job.DownloadPath, "dht_nodes.cache");
                    if (File.Exists(dhtCacheFile))
                    {
                        try
                        {
                            File.Delete(dhtCacheFile);
                            _logger.LogDebug("Cleaned up DHT cache file | JobId: {JobId}", job.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete DHT cache file | JobId: {JobId}", job.Id);
                        }
                    }

                    // Clean up .fresume files
                    var fresumeFiles = Directory.GetFiles(job.DownloadPath, "*.fresume", SearchOption.AllDirectories);
                    foreach (var file in fresumeFiles)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete fresume file {File} | JobId: {JobId}", file, job.Id);
                        }
                    }

                    if (fresumeFiles.Length > 0)
                    {
                        _logger.LogDebug("Cleaned up {Count} fresume files | JobId: {JobId}", fresumeFiles.Length, job.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during torrent cleanup | JobId: {JobId}", job.Id);
                // Don't rethrow - cleanup failures shouldn't prevent cancellation
            }

            return Task.CompletedTask;
        }
    }
}


