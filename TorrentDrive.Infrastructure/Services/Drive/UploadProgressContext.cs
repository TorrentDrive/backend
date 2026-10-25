using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Drive
{


    public class UploadProgressContext(IRedisCacheService redisCache) : IUploadProgressContext
    {
        // Throttling constants
        private const long LogThresholdBytes = 100 * 1024 * 1024; // 100 MB like torrent
        private const double DbUpdateThresholdPercent = 5.0;
        private static readonly TimeSpan ResumeUriTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan CompletedFileTtl = TimeSpan.FromDays(30);
        private static readonly TimeSpan RootFolderTtl = TimeSpan.FromDays(30);

        // State
        private double _lastDbPercent;
        private long _lastDbBytes;
        private DateTime _lastDbTime = DateTime.MinValue;
        private DateTime _lastLogTime = DateTime.MinValue;
        private long _lastLoggedBytes = 0;
        private long _completedBytes;

        // Config
        private int _jobId;
        private int _storageProfileId;
        private long _totalBytes;
        private ILogger? _logger;
        private Func<string, double, double, Task>? _onDbUpdate;
        private bool _isConfigured;

        public bool IsConfigured => _isConfigured;

        public void Configure(int jobId, int storageProfileId, long totalBytes, ILogger logger, Func<string, double, double, Task> onDbUpdate)
        {
            _jobId = jobId;
            _storageProfileId = storageProfileId;
            _totalBytes = totalBytes;
            _logger = logger;
            _onDbUpdate = onDbUpdate;
            _isConfigured = true;

            // Reset state
            _lastDbPercent = 0;
            _lastDbBytes = 0;
            _lastDbTime = DateTime.MinValue;
            _lastLogTime = DateTime.MinValue;
            _lastLoggedBytes = 0;
            _completedBytes = 0;
        }

        public async Task ReportProgressAsync(string fileName, long bytesUploaded, long fileSize)
        {
            if (!_isConfigured) return;

            var currentTotal = _completedBytes + bytesUploaded;
            await CheckAndReportAsync(currentTotal, fileName, isFileComplete: false);
        }

        public async Task MarkFileCompletedAsync(string fileName, long fileSize)
        {
            if (!_isConfigured) return;

            _completedBytes += fileSize;

            // Force check progress to update UI after file finish
            await CheckAndReportAsync(_completedBytes, fileName, isFileComplete: true);
        }

        public async Task MarkBytesCompletedAsync(string fileName, long bytesUploaded)
        {
            if (!_isConfigured) return;

            _completedBytes += bytesUploaded;
            await CheckAndReportAsync(_completedBytes, fileName, isFileComplete: false);
        }

        private async Task CheckAndReportAsync(long currentBytes, string currentFile, bool isFileComplete)
        {
            var now = DateTime.UtcNow;
            var overallPercent = _totalBytes > 0 ? (currentBytes * 100.0) / _totalBytes : 0;

            // 1. DB Update Logic (Throttled or Forced on Completion)
            // If the jump is big enough OR if we just finished a file (good visual checkpoint), update DB
            if (overallPercent - _lastDbPercent >= DbUpdateThresholdPercent || (isFileComplete && overallPercent > _lastDbPercent))
            {
                // Rate since the previous DB write. The first write has no baseline, so
                // it reports 0 rather than an arbitrarily large number.
                var elapsed = _lastDbTime == DateTime.MinValue ? 0 : (now - _lastDbTime).TotalSeconds;
                var speed = elapsed > 0 ? (currentBytes - _lastDbBytes) / elapsed : 0;

                var stateMessage = $"Uploading: {overallPercent:F1}%";
                if (_onDbUpdate != null)
                {
                    await _onDbUpdate(stateMessage, overallPercent, Math.Max(0, speed));
                }

                _lastDbPercent = overallPercent;
                _lastDbBytes = currentBytes;
                _lastDbTime = now;
            }

            // 2. Logging Logic (Byte-based threshold like torrent, or on file complete)
            if (currentBytes - _lastLoggedBytes >= LogThresholdBytes || isFileComplete || _lastLogTime == DateTime.MinValue)
            {
                double speed = 0;
                if (_lastLogTime != DateTime.MinValue && (now - _lastLogTime).TotalSeconds > 0)
                {
                    speed = (currentBytes - _lastLoggedBytes) / (now - _lastLogTime).TotalSeconds;
                }

                _logger?.LogInformation(
                    "Progress | JobId: {JobId} | {Percent:F2}% | {UploadedMB:F2}/{TotalMB:F2} MB | Speed: {SpeedMBps:F2} MB/s",
                    _jobId,
                    overallPercent,
                    currentBytes / (1024.0 * 1024.0),
                    _totalBytes / (1024.0 * 1024.0),
                    speed / (1024.0 * 1024.0));

                _lastLoggedBytes = currentBytes;
                _lastLogTime = now;
            }
        }


        public async Task<string?> GetResumeUriAsync(string relativePath)
        {
            if (!_isConfigured) return null;
            return await redisCache.GetAsync(GetResumeKey(relativePath));
        }

        public async Task SetResumeUriAsync(string relativePath, string resumeUri)
        {
            if (!_isConfigured) return;
            await redisCache.SetAsync(GetResumeKey(relativePath), resumeUri, ResumeUriTtl);
        }

        public async Task ClearResumeUriAsync(string relativePath)
        {
            if (!_isConfigured) return;
            await redisCache.DeleteAsync(GetResumeKey(relativePath));
        }

        public async Task SetCompletedFileAsync(string relativePath, string driveFileId)
        {
            if (!_isConfigured) return;
            await redisCache.SetAsync(GetCompletedFileKey(relativePath), driveFileId, CompletedFileTtl);
        }

        public async Task<string?> GetCompletedFileAsync(string relativePath)
        {
            if (!_isConfigured) return null;
            return await redisCache.GetAsync(GetCompletedFileKey(relativePath));
        }

        public async Task SetRootFolderIdAsync(int jobId, string folderId)
        {
            await redisCache.SetAsync(GetRootFolderKey(jobId), folderId, RootFolderTtl);
        }

        public async Task<string?> GetRootFolderIdAsync(int jobId)
        {
            return await redisCache.GetAsync(GetRootFolderKey(jobId));
        }

        // --- Helpers ---
        //
        // Every progress key carries the storage profile the bytes were written to. A job
        // that fails over to another Drive account therefore never resumes against file or
        // folder IDs that only exist in the account it left.

        private string GetResumeKey(string path) => $"gdrive:resume:{_jobId}:{_storageProfileId}:{SanitizeKey(path)}";
        private string GetCompletedFileKey(string path) => $"gdrive:completed:{_jobId}:{_storageProfileId}:{SanitizeKey(path)}";
        private string GetRootFolderKey(int jobId) => $"gdrive:rootfolder:{jobId}:{_storageProfileId}";

        private static string SanitizeKey(string path)
        {
            var clean = path.Replace('\\', '/');
            if (clean.Length <= 100) return clean;

            // Fast hash for long paths
            var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clean));
            return Convert.ToBase64String(hashBytes)[..20]; // Truncate hash for readability
        }
    }
}