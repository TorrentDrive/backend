namespace TorrentDrive.Core.Options
{

    public class JobHealthMonitorOptions
    {
        /// <summary>
        /// How often to check for orphaned jobs.
        /// Default: 2 minutes.
        /// </summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Jobs not updated in this duration are considered orphaned.
        /// Default: 5 minutes.
        /// </summary>
        public TimeSpan StaleJobThreshold { get; set; } = TimeSpan.FromMinutes(5);
    }
}

