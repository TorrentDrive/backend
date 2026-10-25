namespace TorrentDrive.Core.DTOs.Settings
{
    /// <summary>
    /// Instance-wide settings as shown in the Settings tab. Fields flagged
    /// <c>RequiresRestart</c> are read once at process start; the UI labels them so a user
    /// does not sit waiting for a change that cannot take effect yet.
    /// </summary>
    public record SystemSettingsDto
    {
        public bool EnableFailover { get; init; }
        public int MaxFailoverAttempts { get; init; }
        public int FailureThreshold { get; init; }
        public int HealthCacheTtlSeconds { get; init; }
        public double QuotaHeadroomRatio { get; init; }
        public double DegradedFreeQuotaRatio { get; init; }
        public int ProbeTimeoutSeconds { get; init; }

        public int HangfireWorkerCount { get; init; }
        public bool EnablePrometheus { get; init; }
        public bool EnableTracing { get; init; }

        /// <summary>
        /// Names of the fields above that only take effect after a restart, so the client
        /// does not have to keep its own copy of that list in sync.
        /// </summary>
        public string[] RequiresRestart { get; init; } =
        [
            nameof(HangfireWorkerCount),
            nameof(EnablePrometheus),
            nameof(EnableTracing)
        ];
    }

    public record UpdateSystemSettingsRequestDto
    {
        public bool EnableFailover { get; init; } = true;
        public int MaxFailoverAttempts { get; init; } = 3;
        public int FailureThreshold { get; init; } = 3;
        public int HealthCacheTtlSeconds { get; init; } = 120;
        public double QuotaHeadroomRatio { get; init; } = 0.05;
        public double DegradedFreeQuotaRatio { get; init; } = 0.02;
        public int ProbeTimeoutSeconds { get; init; } = 10;

        public int HangfireWorkerCount { get; init; } = 10;
        public bool EnablePrometheus { get; init; } = true;
        public bool EnableTracing { get; init; } = true;
    }
}
