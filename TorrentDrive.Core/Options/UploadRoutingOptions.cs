namespace TorrentDrive.Core.Options
{
    /// <summary>
    /// Tuning for storage health probing and upload failover.
    /// Bound from the "UploadRouting" configuration section.
    /// </summary>
    public class UploadRoutingOptions
    {
        public const string SectionName = "UploadRouting";

        /// <summary>Master switch for automatic rerouting to a healthy profile.</summary>
        public bool EnableFailover { get; set; } = true;

        /// <summary>
        /// Maximum automatic reroutes per job. Prevents a job from cycling through every
        /// profile a user owns when the real fault is the payload, not the destination.
        /// </summary>
        public int MaxFailoverAttempts { get; set; } = 3;

        /// <summary>How long a probe result stays cached before the provider is called again.</summary>
        public TimeSpan HealthCacheTtl { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Consecutive upload failures before a profile is demoted to Unhealthy.</summary>
        public int FailureThreshold { get; set; } = 3;

        /// <summary>
        /// Free-space headroom required on a candidate profile, as a fraction of the
        /// payload size. 0.05 means a 1 GB upload needs 1.05 GB free.
        /// </summary>
        public double QuotaHeadroomRatio { get; set; } = 0.05;

        /// <summary>
        /// Free quota below this fraction of the account total marks a profile Degraded.
        /// Degraded profiles still accept uploads that fit.
        /// </summary>
        public double DegradedFreeQuotaRatio { get; set; } = 0.02;

        /// <summary>Timeout for a single provider health probe.</summary>
        public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
