using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Interfaces.Hangfire;
using System.Linq.Expressions;
using TorrentDrive.Infrastructure.Workers;

namespace TorrentDrive.GoogleDrive.Worker.Services
{
    public class GoogleDriveUploadJob(
        IStorageTransferOrchestrator transferOrchestrator,
        IUnitOfWork unitOfWork,
        ILogger<GoogleDriveUploadJob> logger,
        IJobStatusService jobStatusService) : UserJobBase<GoogleDriveUploadJob>(unitOfWork, logger, jobStatusService), IGoogleDriveUploadJob
    {
        protected override string LogPrefix => "[GOOGLE_DRIVE:UPLOAD]";

        protected override Expression<Func<UserJob, object?>>[] GetIncludes() =>
        [
            j => j.StorageProfile,
            j => j.User
        ];

        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            await transferOrchestrator.ExecuteTransferAsync(
                job,
                StorageProviderType.GoogleDrive,
                MarkJobFailedAsync,
                cancellationToken);
        }
    }
}