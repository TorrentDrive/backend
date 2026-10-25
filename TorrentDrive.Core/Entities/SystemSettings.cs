namespace TorrentDrive.Core.Entities
{
    /// <summary>
    /// Instance-wide configuration, edited from the Settings tab. Exactly one row exists;
    /// it is created with defaults on first read, seeded from environment variables when
    /// they are present so an install that was configured through the environment keeps
    /// its tuning when it upgrades.
    ///
    /// Everything here used to be an environment variable. The point of moving it is that
    /// a self-hoster should never have to edit a file to change how the app behaves.
    /// </summary>
    public class SystemSettings : BaseEntity
    {
        /// <summary>
        /// When the first-run setup wizard completed. Null means the instance has not been
        /// claimed yet and <c>POST /api/setup/admin</c> will accept a request.
        ///
        /// Stored rather than inferred from "does any user have a password": inference
        /// would silently re-open setup to anyone if the admin row were ever deleted.
        /// </summary>
        public DateTime? SetupCompletedAt { get; set; }

        // --- Upload routing (live; picked up within the settings cache window) ---

        /// <summary>Master switch for automatic rerouting to a healthy profile.</summary>
        public bool EnableFailover { get; set; } = true;

        /// <summary>Maximum automatic reroutes per job.</summary>
        public int MaxFailoverAttempts { get; set; } = 3;

        /// <summary>Consecutive upload failures before a profile is demoted to Unhealthy.</summary>
        public int FailureThreshold { get; set; } = 3;

        /// <summary>How long a health probe result stays cached, in seconds.</summary>
        public int HealthCacheTtlSeconds { get; set; } = 120;

        /// <summary>
        /// Free-space headroom required on a candidate profile, as a fraction of the
        /// payload size. 0.05 means a 1 GB upload needs 1.05 GB free.
        /// </summary>
        public double QuotaHeadroomRatio { get; set; } = 0.05;

        /// <summary>
        /// Free quota below this fraction of the account total marks a profile Degraded.
        /// </summary>
        public double DegradedFreeQuotaRatio { get; set; } = 0.02;

        /// <summary>Timeout for a single provider health probe, in seconds.</summary>
        public int ProbeTimeoutSeconds { get; set; } = 10;

        // --- Read at process start only (the UI must say "restart required") ---

        /// <summary>
        /// Hangfire workers per server, which is the hard ceiling on concurrent transfers:
        /// a download holds its worker for the whole transfer.
        ///
        /// Applied at startup, so a change here only takes effect on restart. The container
        /// entrypoint reads this column on boot.
        /// </summary>
        public int HangfireWorkerCount { get; set; } = 10;

        /// <summary>Expose the Prometheus metrics endpoint. Applied at startup.</summary>
        public bool EnablePrometheus { get; set; } = true;

        /// <summary>Emit OpenTelemetry traces. Applied at startup.</summary>
        public bool EnableTracing { get; set; } = true;
    }
}
