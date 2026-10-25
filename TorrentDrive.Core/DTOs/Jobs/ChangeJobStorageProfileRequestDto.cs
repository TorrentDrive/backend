namespace TorrentDrive.Core.DTOs.Jobs
{
    /// <summary>
    /// Body for pointing a job at a specific storage profile before its upload runs.
    /// </summary>
    public class ChangeJobStorageProfileRequestDto
    {
        /// <summary>Profile the upload should go to. Must belong to the caller and be active.</summary>
        public int StorageProfileId { get; init; }

        /// <summary>
        /// Whether the job may still be rerouted automatically if this profile becomes
        /// unhealthy. Set false to pin the job to this drive and fail instead.
        /// </summary>
        public bool AllowFailover { get; init; } = true;
    }

    /// <summary>
    /// Optional body for a retry, used to send the retry to a different destination.
    /// </summary>
    public class RetryJobRequestDto
    {
        /// <summary>Profile to retry against. Null keeps the job's current destination.</summary>
        public int? StorageProfileId { get; init; }
    }
}
