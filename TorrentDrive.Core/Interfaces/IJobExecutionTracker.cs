using TorrentDrive.Core.Entities.Jobs;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Focused execution tracker seam for background workers to report job start,
    /// byte transfer progress, and live heartbeats without depending on user-facing
    /// job dispatch or query workflows.
    /// </summary>
    public interface IJobExecutionTracker
    {
        Task RecordJobStartedAsync(UserJob job);
        Task RecordTransferProgressAsync(UserJob job, long bytesUploaded, double bytesPerSecond = 0, string? currentState = null);
        Task RecordHeartbeatAsync(int jobId, string? currentState = null);
    }
}
