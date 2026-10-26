namespace TorrentDrive.Core.DTOs.Jobs
{
    public class BatchJobIdsRequestDto
    {
        public List<int>? JobIds { get; set; }
        public int? TargetStorageProfileId { get; set; }
    }

    public class BatchJobActionResultDto
    {
        public int TotalRequested { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public List<int> SucceededJobIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
