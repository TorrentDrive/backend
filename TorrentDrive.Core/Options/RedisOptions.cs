using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>Bound from the "Redis" section. Job state and cancellation signals.</summary>
    public class RedisOptions
    {
        public const string SectionName = "Redis";

        /// <summary>
        /// Not marked Required, and deliberately documented by its .NET key
        /// rather than a friendly name: the all-in-one entrypoint hardcodes
        /// Redis__ConnectionString to the loopback instance, so there is no
        /// operator-facing variable for it there. It only matters in the split
        /// topology, where it is set directly.
        /// </summary>
        [ConfigDoc("Redis__ConnectionString",
            Description = "Redis endpoint. Set directly only in the split topology; the all-in-one image points this at its own bundled Redis and ignores anything you set.",
            Default = "127.0.0.1:6379")]
        public string ConnectionString { get; set; } = "localhost:6379";
    }
}
