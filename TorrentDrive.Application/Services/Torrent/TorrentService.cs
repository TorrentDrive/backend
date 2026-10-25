using Microsoft.Extensions.Configuration;
using TorrentDrive.Core.DTOs.Torrents;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;
using RequestedFile = TorrentDrive.Core.Entities.Torrents.RequestedFile;

namespace TorrentDrive.Application.Services.Torrent
{
    public class TorrentService(
        IUnitOfWork unitOfWork,
        ITrackerScraper trackerScraper,
        ITorrentHealthService torrentHealthService,
        IConfiguration configuration) : ITorrentService
    {
        private readonly string _downloadPath = configuration["TORRENT_DOWNLOAD_PATH"] ?? "/app/downloads";

        /// <summary>
        /// Curated high-availability public trackers augmented onto non-private torrents.
        /// Never injected or scraped when a torrent has the BEP 27 private flag set.
        /// </summary>
        public static readonly IReadOnlyList<string> CuratedPublicTrackers =
        [
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.demonii.com:1337/announce",
            "udp://explodie.org:6969/announce",
            "udp://tracker.openbittorrent.com:6969/announce",
            "udp://tracker.dler.org:6969/announce",
            "udp://opentracker.io:6969/announce",
            "udp://p4p.arenabg.com:1337/announce",
            "http://tracker.opentrackr.org:1337/announce",
            "https://tracker.tamersunion.org:443/announce"
        ];

        public TorrentInfoDto ParseTorrentFile(Stream fileStream)
        {
            if (fileStream.CanSeek && fileStream.Position != 0) fileStream.Position = 0;

            var torrent = MonoTorrent.Torrent.Load(fileStream);

            var hash = torrent.InfoHashes.V1?.ToHex();
            if (string.IsNullOrEmpty(hash))
                throw new ValidationException("V2OnlyNotSupported", "This torrent has no v1 (SHA1) hash. System currently requires v1 support.");

            var trackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (torrent.AnnounceUrls != null)
            {
                foreach (var tier in torrent.AnnounceUrls)
                    foreach (var url in tier)
                        if (!string.IsNullOrWhiteSpace(url)) trackers.Add(url.Trim());
            }

            // BEP 27 Private Torrent protection:
            // Never leak a private torrent's infohash to public fallback trackers.
            if (!torrent.IsPrivate)
            {
                foreach (var fallback in CuratedPublicTrackers)
                    trackers.Add(fallback);
            }

            return new TorrentInfoDto
            {
                Name = torrent.Name,
                InfoHash = hash,
                TotalSize = torrent.Size,
                IsPrivate = torrent.IsPrivate,
                Trackers = [.. trackers],
                Files = [.. torrent.Files.Select((f, index) => new TorrentFileDto
                {
                    Index = index,
                    Path = f.Path,
                    Size = f.Length
                })]
            };
        }

        public async Task<TorrentInfoDto> EnrichWithHealthAsync(TorrentInfoDto torrentInfo)
        {
            var scrapeTrackers = torrentInfo.IsPrivate
                ? torrentInfo.Trackers.Where(t => !CuratedPublicTrackers.Contains(t, StringComparer.OrdinalIgnoreCase))
                : torrentInfo.Trackers;

            var scrape = await trackerScraper.GetScrapeResultsAsync(torrentInfo.InfoHash, scrapeTrackers);
            var health = torrentHealthService.Compute(scrape);
            var healthMultiplier = 1 + (1 - health.HealthScore);

            return torrentInfo with
            {
                HealthScore = health.HealthScore,
                HealthMultiplier = healthMultiplier,
                Health = health,
                ScrapeResult = scrape
            };
        }

        public async Task<TorrentInfoDto> GetTorrentInfoFromTorrentFileAsync(Stream fileStream)
        {
            var torrentInfo = ParseTorrentFile(fileStream);
            return await EnrichWithHealthAsync(torrentInfo);
        }

        public async Task<RequestedFile> FindOrCreateTorrentFile(TorrentInfoDto torrent, int userId, Stream? fileStream = null)
        {
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("UserNotFound", $"User with ID {userId} does not exist. Cannot create RequestedFile.");

            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
                throw new ValidationException("InvalidInfoHash", "InfoHash is required and cannot be empty. Cannot create or find RequestedFile.");

            if (string.IsNullOrWhiteSpace(torrent.Name))
                throw new ValidationException("InvalidFileName", "FileName is required and cannot be empty. Cannot create RequestedFile.");

            if (torrent.TotalSize <= 0)
                throw new ValidationException("InvalidFileSize", "FileSize must be greater than zero. Cannot create RequestedFile.");

            var existingTorrent = await unitOfWork.Repository<RequestedFile>().FirstOrDefaultAsync(t =>
                t.InfoHash == torrent.InfoHash &&
                t.UploadedByUserId == userId);

            if (existingTorrent != null)
                return existingTorrent;

            string? localPath = null;
            if (fileStream != null)
            {
                var torrentsDir = Path.Combine(_downloadPath, "torrents");
                Directory.CreateDirectory(torrentsDir);

                var fileName = $"{torrent.InfoHash}.torrent";
                localPath = Path.Combine(torrentsDir, fileName);

                if (fileStream.CanSeek)
                    fileStream.Position = 0;

                await using var fs = File.Create(localPath);
                await fileStream.CopyToAsync(fs);
            }

            var newTorrentFile = new RequestedFile
            {
                InfoHash = torrent.InfoHash,
                FileName = torrent.Name,
                FileSize = torrent.TotalSize,
                Files = torrent.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId,
                FileType = "Torrent",
                DirectUrl = localPath
            };

            unitOfWork.Repository<RequestedFile>().Add(newTorrentFile);
            await unitOfWork.Complete();

            return newTorrentFile;
        }
    }
}