using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>Bound from the "Hangfire" section. Background job execution.</summary>
    public class HangfireOptions
    {
        public const string SectionName = "Hangfire";

        /// <summary>
        /// A download holds a worker for its entire transfer, so this is a hard
        /// ceiling on concurrent transfers rather than a hint. The entrypoint
        /// prefers the value saved in the Settings tab over the environment
        /// variable, and falls back to the environment only when no setting has
        /// been saved.
        /// </summary>
        [ConfigDoc("HANGFIRE_WORKER_COUNT",
            Description = "How many torrents transfer at once. A download holds a worker for its whole transfer, so this is a hard ceiling. The value saved in the Settings tab wins once you save one.",
            Default = "10")]
        public int WorkerCount { get; set; } = 10;
    }
}
