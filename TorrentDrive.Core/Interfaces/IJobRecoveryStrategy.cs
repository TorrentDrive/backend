using Hangfire;
using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    public interface IJobRecoveryStrategy
    {
        JobType SupportedJobType { get; }
        IReadOnlyList<JobStatus> MonitoredStatuses { get; }
        Task<string?> RecoverJobAsync(IRecoverableJob job, IBackgroundJobClient backgroundJobClient);
    }
}
