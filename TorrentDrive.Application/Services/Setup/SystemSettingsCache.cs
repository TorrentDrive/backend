using TorrentDrive.Core.Options;

namespace TorrentDrive.Application.Services.Setup
{
    /// <summary>
    /// Process-wide cache for the instance settings row.
    ///
    /// Upload routing consults these values on every routing decision and every health
    /// probe. Reading a single-row table that often would be wasteful, so the value is held
    /// here for a short window — long enough to make the reads free, short enough that a
    /// change made in the Settings tab is live without a restart.
    /// </summary>
    public class SystemSettingsCache
    {
        /// <summary>
        /// How stale a cached copy may be. This is the delay between saving a setting and
        /// seeing it take effect, so it is deliberately short.
        /// </summary>
        public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

        private readonly Lock _gate = new();
        private UploadRoutingOptions? _routing;
        private DateTime _loadedAt;

        /// <summary>Cached routing options, or null when nothing is cached or it has expired.</summary>
        public UploadRoutingOptions? Fresh
        {
            get
            {
                lock (_gate)
                {
                    if (_routing == null) return null;
                    return DateTime.UtcNow - _loadedAt <= Ttl ? _routing : null;
                }
            }
        }

        /// <summary>
        /// Last known value regardless of age. Used by the options pipeline, which cannot
        /// go to the database and is better off with a slightly stale value than none.
        /// </summary>
        public UploadRoutingOptions? LastKnown
        {
            get { lock (_gate) return _routing; }
        }

        public void Store(UploadRoutingOptions routing)
        {
            lock (_gate)
            {
                _routing = routing;
                _loadedAt = DateTime.UtcNow;
            }
        }

        /// <summary>Drops the cached copy so the next read goes to the database.</summary>
        public void Invalidate()
        {
            lock (_gate) _routing = null;
        }
    }
}
