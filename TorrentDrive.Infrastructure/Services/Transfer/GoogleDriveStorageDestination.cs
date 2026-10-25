using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Transfer
{
    public class GoogleDriveStorageDestination(
        IGoogleDriveJobService googleDriveService,
        IUploadProgressContext progressContext,
        IUploadRoutingService uploadRoutingService,
        IStorageProfileHealthService storageHealthService,
        IJobExecutionTracker executionTracker,
        ILogger<GoogleDriveStorageDestination> logger) : IStorageDestination
    {
        private const string LogPrefix = "[GOOGLE_DRIVE:ADAPTER]";

        public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;
        public string LockKeyPrefix => "gdrive";
        public string SpeedMetricTag => "googledrive_upload";

        public async Task<DestinationTransferOutcome> TransferFilesAsync(
            UserJob job,
            FileInfo[] files,
            long totalBytes,
            CancellationToken cancellationToken)
        {
            var accessToken = await AuthenticateWithFailoverAsync(job, totalBytes, cancellationToken);
            if (accessToken == null)
            {
                return new DestinationTransferOutcome(files.Length, files.Length, new InvalidOperationException("Failed to authenticate with Google Drive destination."));
            }

            ConfigureProgressContext(job, totalBytes);

            var rootFolderId = await EnsureRootFolderExistsAsync(job, accessToken, cancellationToken);
            var filesToProcess = await FilterAlreadyUploadedFilesAsync(files, job.DownloadPath!);
            var folderIdMap = await CreateFolderHierarchyAsync([.. filesToProcess], job.DownloadPath!, rootFolderId, accessToken, cancellationToken);

            var failedFiles = 0;
            Exception? lastError = null;

            foreach (var file in filesToProcess)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var relDir = Path.GetRelativePath(job.DownloadPath!, file.DirectoryName!);
                var relPath = Path.GetRelativePath(job.DownloadPath!, file.FullName);
                var folderId = folderIdMap.TryGetValue(relDir, out var fid) ? fid : folderIdMap["."];

                try
                {
                    var existingFileId = await googleDriveService.CheckFileExistsAsync(folderId, file.Name, accessToken, cancellationToken);
                    if (!string.IsNullOrEmpty(existingFileId))
                    {
                        await progressContext.SetCompletedFileAsync(relPath, existingFileId);
                        await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{LogPrefix} CheckFileExists failed for {File}, proceeding with upload", LogPrefix, file.Name);
                }

                try
                {
                    var fileId = await googleDriveService.UploadFileAsync(
                        file.FullName, file.Name, folderId, accessToken, relPath, cancellationToken);

                    await progressContext.SetCompletedFileAsync(relPath, fileId);
                    await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                }
                catch (Exception ex)
                {
                    failedFiles++;
                    lastError = ex;
                    logger.LogCritical(ex, "{LogPrefix} Upload failed for {File}", LogPrefix, file.Name);

                    try
                    {
                        var resumeUri = await progressContext.GetResumeUriAsync(relPath);
                        if (!string.IsNullOrEmpty(resumeUri))
                        {
                            var resumedBytes = await googleDriveService.QueryUploadStatusAsync(resumeUri, file.Length, accessToken, cancellationToken);
                            await progressContext.MarkBytesCompletedAsync(file.Name, resumedBytes);
                        }
                    }
                    catch (Exception resumeEx)
                    {
                        logger.LogWarning(resumeEx, "{LogPrefix} Failed to recover partial progress for {File}", LogPrefix, file.Name);
                    }
                }
            }

            return new DestinationTransferOutcome(files.Length, failedFiles, lastError);
        }

        public async Task CleanupOnFailureAsync(UserJob job)
        {
            await googleDriveService.DeleteUploadLockAsync(job.Id);
        }

        private async Task<string?> AuthenticateWithFailoverAsync(UserJob job, long totalBytes, CancellationToken token)
        {
            await executionTracker.RecordHeartbeatAsync(job.Id, "Authenticating...");

            try
            {
                var accessToken = await googleDriveService.GetAccessTokenAsync(job.StorageProfile!, token);
                await executionTracker.RecordHeartbeatAsync(job.Id);
                return accessToken;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{LogPrefix} Authentication failed | JobId: {JobId} | ProfileId: {ProfileId}",
                    LogPrefix, job.Id, job.StorageProfileId);

                var route = await uploadRoutingService.FailoverAsync(job, ex, totalBytes, token);
                if (!route.HasTarget)
                {
                    return null;
                }

                await executionTracker.RecordHeartbeatAsync(job.Id, $"Switched destination to {route.Target!.ProfileName}");

                try
                {
                    var accessToken = await googleDriveService.GetAccessTokenAsync(route.Target, token);
                    await executionTracker.RecordHeartbeatAsync(job.Id);
                    return accessToken;
                }
                catch (Exception retryEx)
                {
                    logger.LogError(retryEx, "{LogPrefix} Authentication failed after reroute | JobId: {JobId} | ProfileId: {ProfileId}",
                        LogPrefix, job.Id, route.Target.Id);

                    await storageHealthService.RecordFailureAsync(route.Target, retryEx, token);
                    return null;
                }
            }
        }

        private void ConfigureProgressContext(UserJob job, long totalBytes)
        {
            progressContext.Configure(
                job.Id,
                job.StorageProfileId,
                totalBytes,
                logger,
                async (stateMessage, percent, bytesPerSecond) =>
                {
                    await executionTracker.RecordTransferProgressAsync(job, (long)(totalBytes * percent / 100.0), bytesPerSecond, stateMessage);
                });
        }

        private async Task<string> EnsureRootFolderExistsAsync(UserJob job, string accessToken, CancellationToken token)
        {
            await executionTracker.RecordHeartbeatAsync(job.Id, "Checking Google Drive folder...");

            var rootId = await progressContext.GetRootFolderIdAsync(job.Id);
            if (!string.IsNullOrEmpty(rootId)) return rootId;

            var folderName = $"Torrent_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var newRootId = await googleDriveService.CreateFolderAsync(folderName, null, accessToken, token);

            await progressContext.SetRootFolderIdAsync(job.Id, newRootId);
            return newRootId;
        }

        private async Task<List<FileInfo>> FilterAlreadyUploadedFilesAsync(FileInfo[] files, string downloadPath)
        {
            var filesToProcess = new List<FileInfo>();

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(downloadPath, file.FullName);
                var completedId = await progressContext.GetCompletedFileAsync(relativePath);

                if (!string.IsNullOrEmpty(completedId))
                {
                    logger.LogDebug("{LogPrefix} Skipping {File} (Already in Redis)", LogPrefix, file.Name);
                    await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                }
                else
                {
                    filesToProcess.Add(file);
                }
            }

            return filesToProcess;
        }

        private async Task<Dictionary<string, string>> CreateFolderHierarchyAsync(
            FileInfo[] files,
            string rootPath,
            string rootId,
            string accessToken,
            CancellationToken token)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = rootId,
                ["."] = rootId
            };

            var uniqueDirs = files
                .Select(f => Path.GetRelativePath(rootPath, f.DirectoryName!))
                .Where(p => p != "." && !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p.Split(Path.DirectorySeparatorChar).Length);

            foreach (var relPath in uniqueDirs)
            {
                if (token.IsCancellationRequested) break;

                var parts = relPath.Split(Path.DirectorySeparatorChar);
                var parentRel = parts.Length > 1 ? string.Join(Path.DirectorySeparatorChar, parts[..^1]) : ".";
                var name = parts[^1];

                var parentId = map.TryGetValue(parentRel, out var pid) ? pid : rootId;

                try
                {
                    var folderId = await googleDriveService.FindOrCreateFolderAsync(name, parentId, accessToken, token);
                    map[relPath] = folderId;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{LogPrefix} Failed to create folder '{Name}', falling back to parent", LogPrefix, name);
                    map[relPath] = parentId;
                }
            }

            return map;
        }
    }
}
