namespace TorrentDrive.Core.DTOs.Common
{
    /// <summary>
    /// What this build actually is. Returned by GET /api/version.
    ///
    /// Anonymous on purpose: the installer checks it after a fresh install, the
    /// UI uses it to show what is running, and support requests are much easier
    /// to triage when a user can paste one URL. It reveals the version and the
    /// commit, both of which are public in a public repository.
    /// </summary>
    public class VersionInfo
    {
        /// <summary>Semantic version of this build, e.g. "1.4.2".</summary>
        public string Version { get; set; } = "unknown";

        /// <summary>Commit the image was built from, when CI stamped one.</summary>
        public string? BuildSha { get; set; }

        /// <summary>When the image was built, when CI stamped one.</summary>
        public string? BuildTime { get; set; }

        /// <summary>
        /// The newest migration recorded in the database. This is the number
        /// that decides whether a rollback is safe: an older build cannot be
        /// trusted against a schema it does not know about.
        /// </summary>
        public string? DatabaseSchema { get; set; }

        /// <summary>Migrations this build knows about but has not applied yet.</summary>
        public int PendingMigrations { get; set; }

        /// <summary>
        /// True when the database has migrations this build has never heard of —
        /// the signature of a rollback past a schema change.
        /// </summary>
        public bool SchemaAhead { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
