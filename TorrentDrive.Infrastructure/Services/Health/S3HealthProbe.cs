using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.DTOs.Storage.S3;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Health
{
    /// <summary>
    /// Verifies an S3-compatible profile with a single-key ListObjectsV2 against the
    /// configured bucket. S3 reports no account quota, so quota fields stay null.
    /// </summary>
    public class S3HealthProbe(ILogger<S3HealthProbe> logger) : IStorageHealthProbe
    {
        public StorageProviderType ProviderType => StorageProviderType.S3;

        public async Task<StorageProbeResult> ProbeAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            S3Credentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<S3Credentials>(profile.CredentialsJson);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "S3 health probe could not read credentials | ProfileId: {ProfileId}", profile.Id);
                return StorageProbeResult.Unhealthy("NotConfigured", "S3 credentials for this profile are unreadable.", requiresUserAction: true);
            }

            if (credentials == null ||
                string.IsNullOrWhiteSpace(credentials.Endpoint) ||
                string.IsNullOrWhiteSpace(credentials.AccessKey) ||
                string.IsNullOrWhiteSpace(credentials.SecretKey) ||
                string.IsNullOrWhiteSpace(credentials.BucketName))
            {
                return StorageProbeResult.Unhealthy("NotConfigured", "S3 profile is missing endpoint, keys, or bucket.", requiresUserAction: true);
            }

            try
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = credentials.Endpoint,
                    ForcePathStyle = true,
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxErrorRetry = 1
                };

                using var client = new AmazonS3Client(credentials.AccessKey, credentials.SecretKey, config);

                await client.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = credentials.BucketName, MaxKeys = 1 },
                    cancellationToken);

                return StorageProbeResult.Healthy();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonS3Exception ex)
            {
                var classified = ClassifyFailure(ex);
                if (classified != null) return classified;

                logger.LogWarning(ex, "S3 health probe failed | ProfileId: {ProfileId}", profile.Id);
                return StorageProbeResult.Unhealthy("ProviderError", $"S3 returned {ex.ErrorCode}: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "S3 health probe failed | ProfileId: {ProfileId}", profile.Id);
                return StorageProbeResult.Unhealthy("Unreachable", $"Could not reach S3 endpoint: {ex.Message}");
            }
        }

        public StorageProbeResult? ClassifyFailure(Exception exception)
        {
            var s3Exception = FindS3Exception(exception);
            if (s3Exception == null) return null;

            return s3Exception.StatusCode switch
            {
                HttpStatusCode.Forbidden => StorageProbeResult.Unhealthy(
                    "AccessDenied", "S3 rejected these credentials for the configured bucket.", requiresUserAction: true),
                HttpStatusCode.Unauthorized => StorageProbeResult.Unhealthy(
                    "AccessDenied", "S3 credentials are invalid.", requiresUserAction: true),
                HttpStatusCode.NotFound => StorageProbeResult.Unhealthy(
                    "BucketNotFound", "The configured S3 bucket does not exist.", requiresUserAction: true),
                HttpStatusCode.InsufficientStorage => StorageProbeResult.Unhealthy(
                    "QuotaExceeded", "The S3 destination is out of space.", requiresUserAction: true),
                _ => null
            };
        }

        private static AmazonS3Exception? FindS3Exception(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                if (current is AmazonS3Exception s3) return s3;
                current = current.InnerException;
            }
            return null;
        }
    }
}
