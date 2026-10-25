namespace TorrentDrive.Core.DTOs.Torrents
{
    public class TorrentHealthMeasurements
    {
        public int Seeders { get; set; }
        public int Leechers { get; set; }
        public int Completed { get; set; }

        public decimal SeederRatio { get; set; }
        public bool IsComplete { get; set; }
        public bool IsDead { get; set; }
        public bool IsWeak { get; set; }
        public bool IsHealthy { get; set; }

        public double HealthScore { get; set; }
    }

}
