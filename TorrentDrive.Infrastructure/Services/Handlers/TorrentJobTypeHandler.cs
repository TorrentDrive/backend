using Hangfire;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Core.Interfaces.Hangfire;

namespace TorrentDrive.Infrastructure.Services.Handlers
{
    /// <summary>
    /// Job type handler for Torrent download operations
    /// </summary>
    public class TorrentJobTypeHandler : IJobTypeHandler
    {
        public JobType JobType => JobType.Torrent;

        public Type GetDownloadJobInterfaceType()
        {
            return typeof(ITorrentDownloadJob);
        }

        public string EnqueueDownloadJob(int jobId, IBackgroundJobClient client)
        {
            return client.Enqueue<ITorrentDownloadJob>(x => x.ExecuteAsync(jobId, CancellationToken.None));
        }

        public bool IsUploadPhaseStatus(JobStatus status)
        {
            return status == JobStatus.PENDING_UPLOAD ||
                   status == JobStatus.UPLOADING ||
                   status == JobStatus.UPLOAD_RETRY ||
                   status == JobStatus.UPLOAD_FAILED ||
                   status == JobStatus.GOOGLE_DRIVE_FAILED;
        }

        public IEnumerable<JobStatus> GetFailedStatuses()
        {
            return new[]
            {
                JobStatus.FAILED,
                JobStatus.TORRENT_FAILED,
                JobStatus.UPLOAD_FAILED,
                JobStatus.GOOGLE_DRIVE_FAILED
            };
        }
    }
}


