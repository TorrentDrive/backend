using TorrentDrive.Core.DTOs.Torrents;

namespace TorrentDrive.Core.Interfaces
{
    public interface ITrackerScraper
    {
        public const string DefaultTrackerUserAgent = "qBittorrent/4.6.7";

        Task<ScrapeAggregationResult> GetScrapeResultsAsync(string infoHash, IEnumerable<string> trackers);
    }
}
