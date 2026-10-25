using TorrentDrive.Core.Entities;

namespace TorrentDrive.Core.Interfaces
{
    public interface IUnitOfWork
    {
        IGenericRepository<T> Repository<T>() where T : BaseEntity;
        Task<int> Complete();
        void Detach<T>(T entity) where T : BaseEntity;
        Task<bool> CanConnectAsync(CancellationToken ct = default);
    }
}
