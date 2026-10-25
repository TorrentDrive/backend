using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>
    /// Values read straight from the configuration root rather than a section.
    /// They are flat environment variables in every deployment, so binding them
    /// into a section would document a key nobody sets.
    /// </summary>
    public class RuntimeOptions
    {
        [ConfigDoc("APPLY_MIGRATIONS",
            Description = "Run pending database migrations at startup.",
            Default = "true")]
        public bool ApplyMigrations { get; set; } = true;

        /// <summary>
        /// Escape hatch for the schema-ahead guard. Starting an older build
        /// against a newer schema is exactly what the guard exists to stop, so
        /// this is deliberately awkward to reach for.
        /// </summary>
        [ConfigDoc("ALLOW_SCHEMA_AHEAD",
            Description = "Start even when the database has migrations this build does not know about. Only safe if the newer migration is additive and this code never touches what it added.",
            Default = "false")]
        public bool AllowSchemaAhead { get; set; }

        [ConfigDoc("TORRENT_DOWNLOAD_PATH",
            Description = "Where torrents are written before upload. Shared by the torrent worker and both upload workers.",
            Default = "/data/downloads")]
        public string TorrentDownloadPath { get; set; } = "/data/downloads";

        [ConfigDoc("FRONTEND_URL",
            Description = "Public address used to build the Google Drive OAuth return URL.",
            Default = "derived from the incoming request")]
        public string? FrontendUrl { get; set; }

        [ConfigDoc("ASPNETCORE_ENVIRONMENT",
            Description = "Standard ASP.NET Core environment name.",
            Default = "Production")]
        public string Environment { get; set; } = "Production";
    }
}
