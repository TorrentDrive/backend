using Microsoft.Extensions.Logging;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Interfaces.Hangfire;
using System.Linq.Expressions;
using TorrentDrive.Infrastructure.Workers;

namespace TorrentDrive.S3.Worker.Services
{
    public class S3UploadJob(
        IStorageTransferOrchestrator transferOrchestrator,
        IUnitOfWork unitOfWork,
        ILogger<S3UploadJob> logger,
        IJobStatusService jobStatusService) : UserJobBase<S3UploadJob>(unitOfWork, logger, jobStatusService), IS3UploadJob
    {
        protected override string LogPrefix => "[S3:UPLOAD]";

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
                StorageProviderType.S3,
                MarkJobFailedAsync,
                cancellationToken);
        }
    }
}