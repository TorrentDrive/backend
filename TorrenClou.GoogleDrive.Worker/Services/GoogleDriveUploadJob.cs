using Microsoft.Extensions.Logging;
using TorrenClou.Core.Entities.Jobs;
using TorrenClou.Core.Enums;
using TorrenClou.Core.Interfaces;
using TorrenClou.Core.Interfaces.Hangfire;
using System.Linq.Expressions;
using TorrenClou.Infrastructure.Workers;

namespace TorrenClou.GoogleDrive.Worker.Services
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