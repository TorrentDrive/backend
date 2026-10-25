using TorrentDrive.Core.DTOs.Torrents;

namespace TorrentDrive.Core.Interfaces
{
    public interface ITorrentHealthService
    {
        TorrentHealthMeasurements Compute(ScrapeAggregationResult scrape);
    }
}