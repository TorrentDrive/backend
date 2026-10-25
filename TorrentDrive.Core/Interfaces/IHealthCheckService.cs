using TorrentDrive.Core.DTOs.Common;

namespace TorrentDrive.Core.Interfaces
{
    public interface IHealthCheckService
    {
        Task<HealthStatus> GetCachedHealthStatusAsync();
        Task<DetailedHealthStatus> GetDetailedHealthStatusAsync(CancellationToken ct = default);
    }
}
