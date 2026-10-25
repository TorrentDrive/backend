using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>
    /// Bound from the "Observability" section. All optional: the bundled
    /// Prometheus and Grafana work without any of it, and these exist only for
    /// shipping telemetry off-box.
    /// </summary>
    public class ObservabilityOptions
    {
        public const string SectionName = "Observability";

        [ConfigDoc("OBSERVABILITY_LOKI_URL",
            Description = "Push structured logs to this Loki instance.",
            Default = "the bundled Loki, or unset outside the all-in-one image")]
        public string? LokiUrl { get; set; }

        [ConfigDoc("OBSERVABILITY_LOKI_USERNAME",
            Description = "Basic-auth user for a hosted Loki.",
            Default = "unset")]
        public string? LokiUsername { get; set; }

        [ConfigDoc("OBSERVABILITY_LOKI_API_KEY",
            Description = "Basic-auth key for a hosted Loki.",
            Default = "unset",
            Secret = true)]
        public string? LokiApiKey { get; set; }

        [ConfigDoc("OBSERVABILITY_OTLP_ENDPOINT",
            Description = "Send traces and metrics to this OTLP collector.",
            Default = "unset")]
        public string? OtlpEndpoint { get; set; }

        [ConfigDoc("OBSERVABILITY_OTLP_HEADERS",
            Description = "Headers for the OTLP exporter, URL-encoded. Usually carries an API key.",
            Default = "unset",
            Secret = true)]
        public string? OtlpHeaders { get; set; }

        [ConfigDoc("OBSERVABILITY_ENABLE_PROMETHEUS",
            Description = "Expose the /metrics endpoint. Overridden by the Settings tab once saved.",
            Default = "true")]
        public bool EnablePrometheus { get; set; } = true;

        [ConfigDoc("OBSERVABILITY_ENABLE_TRACING",
            Description = "Emit distributed traces. Overridden by the Settings tab once saved.",
            Default = "true")]
        public bool EnableTracing { get; set; } = true;

        /// <summary>
        /// Read by OpenTelemetryExtensions but wired to no environment variable
        /// by any compose file or entrypoint, so it has never been settable in
        /// practice. Documented rather than quietly dropped.
        /// </summary>
        [ConfigDoc("OBSERVABILITY_ENABLE_LOGGING",
            Description = "Emit OpenTelemetry logs.",
            Default = "true")]
        public bool EnableLogging { get; set; } = true;
    }
}
