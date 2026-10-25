using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.DTOs.Storage.GoogleDrive;
using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;
using TorrentDrive.Core.Exceptions;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Infrastructure.Services.Health
{
    /// <summary>
    /// Verifies a Google Drive profile by refreshing its token and reading
    /// drive/v3/about, which returns the account storage quota in the same call.
    /// </summary>
    public class GoogleDriveHealthProbe(
        IGoogleDriveJobService googleDriveJobService,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveHealthProbe> logger) : IStorageHealthProbe
    {
        private const string AboutUrl = "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress),storageQuota";

        public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;

        public async Task<StorageProbeResult> ProbeAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            if (profile.NeedsReauth)
            {
                return StorageProbeResult.Unhealthy(
                    "NeedsReauth",
                    "Google Drive access was revoked. Reconnect this account to use it again.",
                    requiresUserAction: true);
            }

            if (!HasRefreshToken(profile))
            {
                return StorageProbeResult.Unhealthy(
                    "NotConfigured",
                    "This Google Drive profile has no refresh token. Finish connecting it first.",
                    requiresUserAction: true);
            }

            string accessToken;
            try
            {
                accessToken = await googleDriveJobService.GetAccessTokenAsync(profile, cancellationToken);
            }
            catch (Exception ex)
            {
                var classified = ClassifyFailure(ex);
                if (classified != null) return classified;

                logger.LogWarning(ex, "Google Drive health probe could not obtain a token | ProfileId: {ProfileId}", profile.Id);
                return StorageProbeResult.Unhealthy("TokenRefreshFailed", $"Could not refresh Google Drive access: {ex.Message}");
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, AboutUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var isQuota = body.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase);

                    return StorageProbeResult.Unhealthy(
                        isQuota ? "QuotaExceeded" : "AccessDenied",
                        isQuota
                            ? "Google Drive is out of storage."
                            : "Google Drive rejected the credentials for this account.",
                        requiresUserAction: true);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogWarning("Google Drive about call failed | ProfileId: {ProfileId} | Status: {Status} | Body: {Body}",
                        profile.Id, response.StatusCode, Truncate(body));

                    return StorageProbeResult.Unhealthy(
                        "ProviderError",
                        $"Google Drive returned {(int)response.StatusCode}.");
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var about = JsonSerializer.Deserialize<DriveAboutResponse>(json);
                var quota = about?.StorageQuota;

                var total = ParseNullableLong(quota?.Limit);
                var used = ParseNullableLong(quota?.Usage);

                return StorageProbeResult.Healthy(total, used);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Google Drive health probe failed | ProfileId: {ProfileId}", profile.Id);
                return StorageProbeResult.Unhealthy("Unreachable", $"Could not reach Google Drive: {ex.Message}");
            }
        }

        public StorageProbeResult? ClassifyFailure(Exception exception)
        {
            foreach (var ex in Unwrap(exception))
            {
                if (ex is ExternalServiceException ese)
                {
                    switch (ese.Code)
                    {
                        case "RefreshTokenExpired":
                        case "NoRefreshToken":
                        case "MissingCredentials":
                            return StorageProbeResult.Unhealthy(
                                "NeedsReauth",
                                "Google Drive access was revoked. Reconnect this account to use it again.",
                                requiresUserAction: true);
                    }
                }

                var message = ex.Message ?? string.Empty;

                if (message.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase))
                {
                    return StorageProbeResult.Unhealthy(
                        "QuotaExceeded",
                        "Google Drive is out of storage.",
                        requiresUserAction: true);
                }

                if (message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("Invalid Credentials", StringComparison.OrdinalIgnoreCase))
                {
                    return StorageProbeResult.Unhealthy(
                        "NeedsReauth",
                        "Google Drive rejected the stored credentials. Reconnect this account.",
                        requiresUserAction: true);
                }
            }

            return null;
        }

        private static IEnumerable<Exception> Unwrap(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                yield return current;

                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                        yield return inner;
                }

                current = current.InnerException;
            }
        }

        private static bool HasRefreshToken(UserStorageProfile profile)
        {
            try
            {
                var credentials = JsonSerializer.Deserialize<GoogleDriveCredentials>(profile.CredentialsJson);
                return !string.IsNullOrEmpty(credentials?.RefreshToken);
            }
            catch
            {
                return false;
            }
        }

        private static long? ParseNullableLong(string? value) =>
            long.TryParse(value, out var parsed) ? parsed : null;

        private static string Truncate(string value) =>
            value.Length <= 500 ? value : value[..500];

        private sealed class DriveAboutResponse
        {
            [JsonPropertyName("storageQuota")]
            public DriveStorageQuota? StorageQuota { get; set; }
        }

        private sealed class DriveStorageQuota
        {
            /// <summary>Total bytes. Absent on unlimited (pooled/enterprise) accounts.</summary>
            [JsonPropertyName("limit")]
            public string? Limit { get; set; }

            [JsonPropertyName("usage")]
            public string? Usage { get; set; }
        }
    }
}
