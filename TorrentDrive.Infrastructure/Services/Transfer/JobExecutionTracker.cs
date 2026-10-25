using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Transfer
{
    public class JobExecutionTracker(IUnitOfWork unitOfWork) : IJobExecutionTracker
    {
        public async Task RecordJobStartedAsync(UserJob job)
        {
            job.StartedAt ??= DateTime.UtcNow;
            job.LastHeartbeat = DateTime.UtcNow;
            await unitOfWork.Complete();
        }

        public async Task RecordTransferProgressAsync(UserJob job, long bytesUploaded, double bytesPerSecond = 0, string? currentState = null)
        {
            job.BytesUploaded = bytesUploaded;
            job.UploadSpeedBytesPerSecond = bytesPerSecond;
            if (currentState != null)
            {
                job.CurrentState = currentState;
            }
            job.LastHeartbeat = DateTime.UtcNow;
            await unitOfWork.Complete();
        }

        public async Task RecordHeartbeatAsync(int jobId, string? currentState = null)
        {
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
            if (job == null || (job.Status != JobStatus.UPLOADING && job.Status != JobStatus.DOWNLOADING))
                return;

            job.LastHeartbeat = DateTime.UtcNow;
            if (currentState != null)
            {
                job.CurrentState = currentState;
            }
            await unitOfWork.Complete();
        }
    }
}
