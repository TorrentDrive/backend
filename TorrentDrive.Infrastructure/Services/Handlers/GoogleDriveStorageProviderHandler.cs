using Hangfire;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Interfaces.Hangfire;

namespace TorrentDrive.Infrastructure.Services.Handlers
{
    /// <summary>
    /// Storage provider handler for Google Drive operations
    /// </summary>
    public class GoogleDriveStorageProviderHandler : IStorageProviderHandler
    {
        private readonly IRedisLockService _redisLockService;
        private readonly ILogger<GoogleDriveStorageProviderHandler> _logger;

        public GoogleDriveStorageProviderHandler(
            IRedisLockService redisLockService,
            ILogger<GoogleDriveStorageProviderHandler> logger)
        {
            _redisLockService = redisLockService;
            _logger = logger;
        }

        public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            try
            {
                var lockKey = $"gdrive:lock:{jobId}";
                var result = await _redisLockService.DeleteLockAsync(lockKey);
                
                if (result)
                {
                    _logger.LogDebug("Deleted Google Drive upload lock | JobId: {JobId}", jobId);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Google Drive upload lock | JobId: {JobId}", jobId);
                return false;
            }
        }

        public Type GetUploadJobInterfaceType()
        {
            return typeof(IGoogleDriveUploadJob);
        }

        public string EnqueueUploadJob(int jobId, IBackgroundJobClient client)
        {
            return client.Enqueue<IGoogleDriveUploadJob>(x => x.ExecuteAsync(jobId, CancellationToken.None));
        }
    }
}


