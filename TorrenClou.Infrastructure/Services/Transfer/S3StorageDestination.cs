using System.Buffers;
using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Entities.Torrents;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Exceptions;
using TorrenClou.Core.Interfaces;
using TorrenClou.Core.DTOs.Storage.S3;

namespace TorrenClou.Infrastructure.Services.Transfer
{
    public class S3StorageDestination(
        IS3JobService s3JobService,
        ILoggerFactory loggerFactory,
        IJobExecutionTracker executionTracker,
        ILogger<S3StorageDestination> logger) : IStorageDestination
    {
        private const string LogPrefix = "[S3:ADAPTER]";
        private const long PartSize = 10 * 1024 * 1024; // 10 MB
        private const int ProgressUpdateIntervalSeconds = 10;

        private IS3ResumableUploadService? _activeUploadService;
        private string? _activeBucketName;

        public StorageProviderType ProviderType => StorageProviderType.S3;
        public string LockKeyPrefix => "s3";
        public string SpeedMetricTag => "s3_upload";

        public async Task<DestinationTransferOutcome> TransferFilesAsync(
            UserJob job,
            FileInfo[] files,
            long totalBytes,
            CancellationToken cancellationToken)
        {
            var (accessKey, secretKey, endpoint, bucketName) = await s3JobService.VerifyAndGetCredentialsAsync(job.StorageProfile!, cancellationToken);

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true
            };

            using var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
            var s3UploadService = new S3ResumableUploadService(s3Client, loggerFactory.CreateLogger<S3ResumableUploadService>());

            _activeUploadService = s3UploadService;
            _activeBucketName = bucketName;

            try
            {
                var overallBytesUploaded = 0L;
                var lastProgressUpdate = DateTime.UtcNow;
                var lastReportedBytes = 0L;

                for (int i = 0; i < files.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var file = files[i];
                    var relativePath = Path.GetRelativePath(job.DownloadPath!, file.FullName);
                    var s3Key = $"torrents/{job.Id}/{relativePath.Replace('\\', '/')}";

                    logger.LogInformation("{LogPrefix} File {Index}/{Total} | {FileName} | {SizeMB:F2} MB",
                        LogPrefix, i + 1, files.Length, file.Name, file.Length / (1024.0 * 1024.0));

                    try
                    {
                        await UploadFileWithResumeAsync(job, file, s3Key, bucketName, s3UploadService, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return new DestinationTransferOutcome(files.Length, files.Length - i, ex);
                    }

                    overallBytesUploaded += file.Length;

                    var progressNow = DateTime.UtcNow;
                    if ((progressNow - lastProgressUpdate).TotalSeconds >= ProgressUpdateIntervalSeconds)
                    {
                        var elapsed = (progressNow - lastProgressUpdate).TotalSeconds;
                        var speed = elapsed > 0 ? (overallBytesUploaded - lastReportedBytes) / elapsed : 0;

                        await executionTracker.RecordTransferProgressAsync(job, overallBytesUploaded, Math.Max(0, speed));

                        lastReportedBytes = overallBytesUploaded;
                        lastProgressUpdate = progressNow;
                    }
                }

                return new DestinationTransferOutcome(files.Length, 0, null);
            }
            finally
            {
                _activeUploadService = null;
                _activeBucketName = null;
            }
        }

        public async Task CleanupOnFailureAsync(UserJob job)
        {
            if (_activeUploadService != null && _activeBucketName != null)
            {
                try
                {
                    var inProgressUploads = await s3JobService.GetInProgressUploadsAsync(job.Id);
                    foreach (var upload in inProgressUploads)
                    {
                        if (!string.IsNullOrEmpty(upload.UploadId))
                        {
                            try
                            {
                                await _activeUploadService.AbortUploadAsync(_activeBucketName, upload.S3Key, upload.UploadId);
                            }
                            catch (Exception abortEx)
                            {
                                logger.LogWarning(abortEx, "{LogPrefix} Failed to abort multipart upload | JobId: {JobId} | Key: {Key}",
                                    LogPrefix, job.Id, upload.S3Key);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{LogPrefix} Failed to query/abort multipart uploads | JobId: {JobId}", LogPrefix, job.Id);
                }
            }

            await s3JobService.DeleteUploadLockAsync(job.Id);
        }

        private async Task UploadFileWithResumeAsync(
            UserJob job,
            FileInfo file,
            string s3Key,
            string bucketName,
            IS3ResumableUploadService s3UploadService,
            CancellationToken cancellationToken)
        {
            if (await CheckFileAlreadyUploadedAsync(bucketName, s3Key, s3UploadService, cancellationToken))
                return;

            var (progress, uploadId, existingParts) = await LoadOrCreateProgressAsync(
                job, file, s3Key, bucketName, s3UploadService, cancellationToken);

            var allParts = await UploadAllPartsAsync(
                job, file, s3Key, bucketName, uploadId, progress, existingParts, s3UploadService, cancellationToken);

            await s3UploadService.CompleteUploadAsync(bucketName, s3Key, uploadId, allParts, cancellationToken);

            progress.Status = S3UploadProgressStatus.Completed;
            progress.CompletedAt = DateTime.UtcNow;

            await s3JobService.DeleteUploadProgressAsync(progress);
        }

        private async Task<bool> CheckFileAlreadyUploadedAsync(
            string bucketName,
            string s3Key,
            IS3ResumableUploadService s3UploadService,
            CancellationToken cancellationToken)
        {
            try
            {
                return await s3UploadService.CheckObjectExistsAsync(bucketName, s3Key, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{LogPrefix} CheckObjectExists failed for {Key}, proceeding with upload", LogPrefix, s3Key);
                return false;
            }
        }

        private async Task<(S3SyncProgress progress, string uploadId, List<PartETag> existingParts)> LoadOrCreateProgressAsync(
            UserJob job,
            FileInfo file,
            string s3Key,
            string bucketName,
            IS3ResumableUploadService s3UploadService,
            CancellationToken cancellationToken)
        {
            var existingProgress = await s3JobService.GetUploadProgressAsync(job.Id, s3Key);

            S3SyncProgress? progress = existingProgress;
            string? uploadId = null;
            List<PartETag>? existingParts = null;

            if (progress != null && progress.Status == S3UploadProgressStatus.InProgress && !string.IsNullOrEmpty(progress.UploadId))
            {
                uploadId = progress.UploadId;
                existingParts = ParsePartETags(progress.PartETags);

                try
                {
                    var s3Parts = await s3UploadService.ListPartsAsync(bucketName, s3Key, uploadId, cancellationToken);
                    existingParts = MergePartETags(existingParts, s3Parts);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{LogPrefix} Remote upload session not found, restarting | Key: {Key}", LogPrefix, s3Key);
                    uploadId = null;
                }
            }

            if (string.IsNullOrEmpty(uploadId))
            {
                if (existingProgress != null)
                {
                    try { await s3JobService.DeleteUploadProgressAsync(existingProgress); }
                    catch { /* ignore */ }
                    progress = null;
                }

                uploadId = await s3UploadService.InitiateUploadAsync(bucketName, s3Key, file.Length, cancellationToken: cancellationToken);
                var totalParts = (int)Math.Ceiling((double)file.Length / PartSize);

                progress = new S3SyncProgress
                {
                    JobId = job.Id,
                    LocalFilePath = file.FullName,
                    S3Key = s3Key,
                    UploadId = uploadId,
                    PartSize = PartSize,
                    TotalParts = totalParts,
                    Status = S3UploadProgressStatus.InProgress,
                    StartedAt = DateTime.UtcNow,
                    TotalBytes = file.Length
                };

                await s3JobService.CreateUploadProgressAsync(progress);
                existingParts = [];
            }
            else
            {
                progress!.UpdatedAt = DateTime.UtcNow;
                await s3JobService.SaveUploadProgressAsync(progress);
                existingParts ??= [];
            }

            return (progress, uploadId, existingParts);
        }

        private async Task<List<PartETag>> UploadAllPartsAsync(
            UserJob job,
            FileInfo file,
            string s3Key,
            string bucketName,
            string uploadId,
            S3SyncProgress progress,
            List<PartETag> existingParts,
            IS3ResumableUploadService s3UploadService,
            CancellationToken cancellationToken)
        {
            await using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var uploadedPartNumbers = new HashSet<int>(existingParts.Select(p => p.PartNumber));
            var buffer = ArrayPool<byte>.Shared.Rent((int)PartSize);

            try
            {
                for (int partNumber = 1; partNumber <= progress.TotalParts; partNumber++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (uploadedPartNumbers.Contains(partNumber))
                        continue;

                    var partStart = (partNumber - 1) * PartSize;
                    var currentPartSize = (int)Math.Min(PartSize, file.Length - partStart);

                    fileStream.Seek(partStart, SeekOrigin.Begin);
                    var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, currentPartSize), cancellationToken);

                    if (bytesRead != currentPartSize)
                    {
                        throw new ExternalServiceException("ReadError", $"Read mismatch part {partNumber}: expected {currentPartSize} bytes, got {bytesRead}");
                    }

                    using var partStream = new MemoryStream(buffer, 0, bytesRead);
                    var partETag = await s3UploadService.UploadPartAsync(
                        bucketName, s3Key, uploadId, partNumber, partStream, cancellationToken);

                    existingParts.Add(partETag);

                    progress.PartsCompleted = existingParts.Count;
                    progress.BytesUploaded = Math.Min(progress.PartsCompleted * PartSize, file.Length);
                    progress.PartETags = JsonSerializer.Serialize(existingParts);
                    progress.UpdatedAt = DateTime.UtcNow;

                    await s3JobService.SaveUploadProgressAsync(progress);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            existingParts.Sort((a, b) => a.PartNumber.CompareTo(b.PartNumber));
            return existingParts;
        }

        private static List<PartETag> MergePartETags(List<PartETag>? stored, List<PartETag> s3Parts)
        {
            var merged = new Dictionary<int, PartETag>();
            foreach (var p in s3Parts) merged[p.PartNumber] = p;

            if (stored != null)
            {
                foreach (var p in stored)
                {
                    if (!merged.ContainsKey(p.PartNumber)) merged[p.PartNumber] = p;
                }
            }

            return merged.Values.OrderBy(p => p.PartNumber).ToList();
        }

        private static List<PartETag> ParsePartETags(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PartETag>>(json) ?? []; }
            catch { return []; }
        }
    }
}
