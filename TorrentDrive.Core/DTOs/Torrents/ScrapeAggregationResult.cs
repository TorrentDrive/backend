namespace TorrentDrive.Core.DTOs.Torrents
{
    public class ScrapeAggregationResult
    {
        public int Seeders { get; set; }
        public int Leechers { get; set; }
        public int Completed { get; set; }

        public int TrackersSuccess { get; set; }
        public int TrackersTotal { get; set; }
    }
}