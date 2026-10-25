using System.Reflection;

namespace TorrentDrive.Core
{
    /// <summary>
    /// What this assembly was built as.
    ///
    /// Every project used to report 1.0.0.0 because nothing set a version, so
    /// the value surfaced by the health endpoints was a framework default rather
    /// than a fact. The version now comes from Directory.Build.props, and CI
    /// stamps the commit and build time as assembly metadata.
    /// </summary>
    public static class BuildInfo
    {
        private static readonly Assembly? Entry = Assembly.GetEntryAssembly();

        /// <summary>
        /// Semantic version, e.g. "1.4.2". Prefers InformationalVersion, which
        /// carries the full string; AssemblyVersion is limited to four numbers
        /// and would drop a prerelease suffix.
        /// </summary>
        public static string Version { get; } = ResolveVersion();

        /// <summary>
        /// Commit the build came from. Prefers the value CI stamps, and falls
        /// back to the suffix the SDK appends to InformationalVersion
        /// ("1.4.2+abc123"), which means a plain `dotnet build` reports it too.
        /// </summary>
        public static string? Sha { get; } = Metadata("BuildSha") ?? ShaFromInformationalVersion();

        /// <summary>Build timestamp, or null outside CI.</summary>
        public static string? Time { get; } = Metadata("BuildTime");

        private static string ResolveVersion()
        {
            try
            {
                var informational = Entry
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informational))
                {
                    // The SDK appends "+<commit>" to InformationalVersion. The
                    // commit is reported separately, so trim it off here.
                    var plus = informational.IndexOf('+');
                    return plus > 0 ? informational[..plus] : informational;
                }

                return Entry?.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string? ShaFromInformationalVersion()
        {
            try
            {
                var informational = Entry
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (string.IsNullOrWhiteSpace(informational)) return null;

                var plus = informational.IndexOf('+');
                if (plus < 0 || plus == informational.Length - 1) return null;

                return informational[(plus + 1)..];
            }
            catch
            {
                return null;
            }
        }

        private static string? Metadata(string key)
        {
            try
            {
                var value = Entry
                    ?.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == key)
                    ?.Value;

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }
    }
}
