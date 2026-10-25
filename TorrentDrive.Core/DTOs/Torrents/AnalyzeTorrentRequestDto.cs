using Microsoft.AspNetCore.Http;

namespace TorrentDrive.Core.DTOs.Torrents
{
    public class AnalyzeTorrentRequestDto
    {
        public IFormFile TorrentFile { get; set; } = null!;
    }
}
