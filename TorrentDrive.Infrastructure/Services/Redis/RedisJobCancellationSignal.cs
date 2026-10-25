using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Redis
{
    /// <summary>
    /// Redis-backed implementation of IJobCancellationSignal.
    ///
    /// Sets a short-lived Redis key when the API cancels a job so that the worker
    /// process (running in a separate Docker container) can detect the signal inside
    /// its monitoring loop and stop gracefully.
    ///
    /// Key format : jobs:cancel:{jobId}
    /// TTL        : 2 hours (auto-expires if the worker never picks it up)
    /// </summary>
    public class RedisJobCancellationSignal(
        IConnectionMultiplexer redis,
        ILogger<RedisJobCancellationSignal> logger) : IJobCancellationSignal
    {
        private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(2);
        private IDatabase Db => redis.GetDatabase();

        private static string Key(int jobId) => $"jobs:cancel:{jobId}";

        public async Task SignalAsync(int jobId)
        {
            await Db.StringSetAsync(Key(jobId), "1", KeyTtl);
            logger.LogInformation("Cancellation signal set | JobId: {JobId}", jobId);
        }

        public async Task<bool> IsCancelledAsync(int jobId)
        {
            return await Db.KeyExistsAsync(Key(jobId));
        }

        public async Task ClearAsync(int jobId)
        {
            await Db.KeyDeleteAsync(Key(jobId));
            logger.LogDebug("Cancellation signal cleared | JobId: {JobId}", jobId);
        }
    }
}
