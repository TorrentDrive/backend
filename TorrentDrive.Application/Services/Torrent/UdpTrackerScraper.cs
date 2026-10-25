using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MonoTorrent.BEncoding;
using TorrentDrive.Core.DTOs.Torrents;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services.Torrent
{
    /// <summary>
    /// Multi-protocol tracker scraper supporting BEP 15 (UDP) and BEP 48 (HTTP/HTTPS)
    /// with bounded DNS/socket timeouts and mainstream BitTorrent client headers.
    /// </summary>
    public class UdpTrackerScraper(IHttpClientFactory httpClientFactory) : ITrackerScraper
    {
        private const long PROTOCOL_ID = 0x41727101980;
        private const string ClientUserAgent = ITrackerScraper.DefaultTrackerUserAgent;
        private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan HttpScrapeTimeout = TimeSpan.FromSeconds(3.5);
        private const int MaxConcurrentTrackers = 20;

        public async Task<ScrapeAggregationResult> GetScrapeResultsAsync(string infoHash, IEnumerable<string> trackers)
        {
            if (trackers == null) throw new ArgumentNullException(nameof(trackers));

            var hashBytes = TryParseInfoHash(infoHash);
            if (hashBytes == null)
            {
                return new ScrapeAggregationResult();
            }

            var trackerList = trackers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Where(t =>
                    t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (trackerList.Count == 0)
            {
                return new ScrapeAggregationResult();
            }

            using var throttler = new SemaphoreSlim(MaxConcurrentTrackers);

            var tasks = trackerList.Select(async t =>
            {
                await throttler.WaitAsync();
                try
                {
                    if (t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                        return await ScrapeUdpTrackerAsync(t, hashBytes);

                    return await ScrapeHttpTrackerAsync(t, hashBytes);
                }
                finally
                {
                    throttler.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            int trackerCount = results.Length;
            var successful = results.Where(r => r.Sucess).ToArray();

            if (successful.Length == 0)
            {
                return new ScrapeAggregationResult
                {
                    Seeders = 0,
                    Leechers = 0,
                    Completed = 0,
                    TrackersSuccess = 0,
                    TrackersTotal = trackerCount
                };
            }

            return new ScrapeAggregationResult
            {
                Seeders = successful.Max(r => r.Seeders),
                Leechers = successful.Max(r => r.Leechers),
                Completed = successful.Max(r => r.Completed),
                TrackersSuccess = successful.Length,
                TrackersTotal = trackerCount
            };
        }

        private async Task<ScrapeResult> ScrapeUdpTrackerAsync(string trackerUrl, byte[] infoHash)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var udpClient = new UdpClient();

            try
            {
                var uri = new Uri(trackerUrl);

                using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                dnsCts.CancelAfter(StepTimeout);
                var ipAddresses = await Dns.GetHostAddressesAsync(uri.Host, dnsCts.Token);

                var ip =
                    ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ??
                    ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetworkV6);

                if (ip == null)
                    return new ScrapeResult(0, 0, 0, false);

                udpClient.Connect(ip, uri.Port);

                // 1. BEP 15 CONNECT
                int connectTransactionId = Random.Shared.Next();
                var connectReq = new byte[16];
                BinaryPrimitives.WriteInt64BigEndian(connectReq.AsSpan(0, 8), PROTOCOL_ID);
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(8, 4), 0); // action = connect
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(12, 4), connectTransactionId);

                await udpClient.SendAsync(connectReq, cts.Token);

                var connectRes = await ReceiveWithTimeoutAsync(udpClient, cts.Token);
                if (connectRes.Length < 16)
                    return new ScrapeResult(0, 0, 0, false);

                var action = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(0, 4));
                var transIdRes = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(4, 4));

                if (action != 0 || transIdRes != connectTransactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectRes.AsSpan(8, 8));

                // 2. BEP 15 SCRAPE
                int scrapeTransactionId = Random.Shared.Next();
                var scrapeReq = new byte[36];
                BinaryPrimitives.WriteInt64BigEndian(scrapeReq.AsSpan(0, 8), connectionId);
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(8, 4), 2); // action = scrape
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(12, 4), scrapeTransactionId);
                Buffer.BlockCopy(infoHash, 0, scrapeReq, 16, 20);

                await udpClient.SendAsync(scrapeReq, cts.Token);

                var scrapeRes = await ReceiveWithTimeoutAsync(udpClient, cts.Token);
                if (scrapeRes.Length < 20)
                    return new ScrapeResult(0, 0, 0, false);

                var action2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(0, 4));
                var transId2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(4, 4));

                if (action2 != 2 || transId2 != scrapeTransactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var seeders = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(8, 4));
                var completed = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(12, 4));
                var leechers = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(16, 4));

                return new ScrapeResult(
                    Math.Max(0, seeders),
                    Math.Max(0, leechers),
                    Math.Max(0, completed),
                    true);
            }
            catch
            {
                return new ScrapeResult(0, 0, 0, false);
            }
        }

        private async Task<ScrapeResult> ScrapeHttpTrackerAsync(string announceUrl, byte[] infoHash)
        {
            try
            {
                var scrapeUrl = BuildHttpScrapeUrl(announceUrl, infoHash);
                if (scrapeUrl == null)
                    return new ScrapeResult(0, 0, 0, false);

                using var cts = new CancellationTokenSource(HttpScrapeTimeout);
                var client = httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, scrapeUrl);
                request.Headers.UserAgent.ParseAdd(ClientUserAgent);
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return new ScrapeResult(0, 0, 0, false);

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                if (BEncodedValue.Decode(bytes) is not BEncodedDictionary root)
                    return new ScrapeResult(0, 0, 0, false);

                if (!root.TryGetValue(new BEncodedString("files"), out var filesVal) ||
                    filesVal is not BEncodedDictionary filesDict ||
                    filesDict.Count == 0)
                {
                    return new ScrapeResult(0, 0, 0, false);
                }

                // Match the 20-byte infohash key or take the first entry returned for our single-hash query
                BEncodedDictionary? statsDict = null;
                var targetKey = new BEncodedString(infoHash);
                if (filesDict.TryGetValue(targetKey, out var exactMatch) && exactMatch is BEncodedDictionary exactDict)
                {
                    statsDict = exactDict;
                }
                else if (filesDict.Values.FirstOrDefault() is BEncodedDictionary firstDict)
                {
                    statsDict = firstDict;
                }

                if (statsDict == null)
                    return new ScrapeResult(0, 0, 0, false);

                int seeders = statsDict.TryGetValue(new BEncodedString("complete"), out var c) && c is BEncodedNumber cn ? (int)cn.Number : 0;
                int downloaded = statsDict.TryGetValue(new BEncodedString("downloaded"), out var d) && d is BEncodedNumber dn ? (int)dn.Number : 0;
                int leechers = statsDict.TryGetValue(new BEncodedString("incomplete"), out var i) && i is BEncodedNumber inum ? (int)inum.Number : 0;

                return new ScrapeResult(
                    Math.Max(0, seeders),
                    Math.Max(0, leechers),
                    Math.Max(0, downloaded),
                    true);
            }
            catch
            {
                return new ScrapeResult(0, 0, 0, false);
            }
        }

        private static string? BuildHttpScrapeUrl(string announceUrl, byte[] infoHash)
        {
            // BEP 48: Find the last '/' in the announce URL. If the text immediately following
            // that '/' starts with "announce", replace "announce" with "scrape".
            int lastSlash = announceUrl.LastIndexOf('/');
            if (lastSlash < 0) return null;

            var tail = announceUrl[(lastSlash + 1)..];
            if (!tail.StartsWith("announce", StringComparison.OrdinalIgnoreCase))
                return null;

            var scrapeBase = string.Concat(
                announceUrl.AsSpan(0, lastSlash + 1),
                "scrape",
                tail.AsSpan("announce".Length));

            var encodedHash = UrlEncodeBinary(infoHash);
            var separator = scrapeBase.Contains('?') ? "&" : "?";
            return $"{scrapeBase}{separator}info_hash={encodedHash}";
        }

        private static string UrlEncodeBinary(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                if ((b >= 'a' && b <= 'z') ||
                    (b >= 'A' && b <= 'Z') ||
                    (b >= '0' && b <= '9') ||
                    b == '.' || b == '-' || b == '_' || b == '~')
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2"));
                }
            }
            return sb.ToString();
        }

        private static async Task<byte[]> ReceiveWithTimeoutAsync(UdpClient client, CancellationToken outerToken)
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            stepCts.CancelAfter(StepTimeout);
            var result = await client.ReceiveAsync(stepCts.Token);
            return result.Buffer;
        }

        private static byte[]? TryParseInfoHash(string infoHash)
        {
            if (string.IsNullOrWhiteSpace(infoHash))
                return null;

            infoHash = infoHash.Trim();
            if (infoHash.Length != 40)
                return null;

            try
            {
                var bytes = Convert.FromHexString(infoHash);
                return bytes.Length == 20 ? bytes : null;
            }
            catch
            {
                return null;
            }
        }
    }
}