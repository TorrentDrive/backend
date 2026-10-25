namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Provides a distributed cancellation signal for running jobs.
    /// Used to notify workers (which run in separate processes/containers)
    /// that a job has been cancelled via the API.
    ///
    /// BackgroundJob.Delete() only prevents a queued job from starting — it does NOT
    /// cancel the CancellationToken of an already-executing Hangfire job. This signal
    /// bridges that gap using Redis so the worker can detect user-initiated cancellation
    /// and stop gracefully.
    /// </summary>
    public interface IJobCancellationSignal
    {
        /// <summary>Publish a cancellation signal for the given job.</summary>
        Task SignalAsync(int jobId);

        /// <summary>Returns true if a cancellation signal exists for the given job.</summary>
        Task<bool> IsCancelledAsync(int jobId);

        /// <summary>Remove the cancellation signal once the worker has stopped.</summary>
        Task ClearAsync(int jobId);
    }
}
