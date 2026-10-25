using TorrentDrive.Core.DTOs.Torrents;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services.Torrent
{
    public class TorrentAnalysisService(
        ITorrentService torrentService) : ITorrentAnalysisService
    {
        public async Task<TorrentAnalysisResponseDto> AnalyzeTorrentAsync(AnalyzeTorrentRequestDto request, int userId, Stream torrentFile)
        {
            if (torrentFile == null || torrentFile.Length == 0)
                throw new ValidationException("Invalid", "No torrent file provided.");

            var fileExtension = Path.GetExtension(request.TorrentFile.FileName).ToLowerInvariant();
            if (fileExtension != ".torrent")
                throw new ValidationException("InvalidTorrent", "Invalid file format. Only .torrent files are accepted.");

            var torrentInfo = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFile);

            if (string.IsNullOrEmpty(torrentInfo.InfoHash))
                throw new ValidationException("InvalidTorrent", "Failed to parse torrent info.");

            torrentFile.Position = 0;
            var torrentStored = await torrentService.FindOrCreateTorrentFile(torrentInfo, userId, torrentFile);

            return new TorrentAnalysisResponseDto
            {
                TorrentFileId = torrentStored.Id,
                FileName = torrentStored.FileName,
                InfoHash = torrentStored.InfoHash,
                TotalSizeInBytes = torrentInfo.TotalSize,
                IsPrivate = torrentInfo.IsPrivate,
                Files = torrentInfo.Files,
                TorrentHealth = torrentInfo.Health,
            };
        }
    }
}