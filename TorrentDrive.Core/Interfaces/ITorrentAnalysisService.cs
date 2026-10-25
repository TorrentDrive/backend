using TorrentDrive.Core.DTOs.Torrents;

namespace TorrentDrive.Core.Interfaces
{
    public interface ITorrentAnalysisService
    {
        Task<TorrentAnalysisResponseDto> AnalyzeTorrentAsync(
            AnalyzeTorrentRequestDto request,
            int userId,
            Stream torrentFile);
    }
}
