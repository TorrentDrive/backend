using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Infrastructure.Repositories;

namespace TorrentDrive.Infrastructure.Data
{
    public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
    {
        private readonly ConcurrentDictionary<Type, object> _repositories = new();

        public async Task<int> Complete()
        {
            return await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            context.Dispose();
        }

        public IGenericRepository<T> Repository<T>() where T : BaseEntity
        {
            return (IGenericRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new GenericRepository<T>(context));
        }

        public void Detach<T>(T entity) where T : BaseEntity
        {
            var entry = context.Entry(entity);
            if (entry != null)
            {
                entry.State = EntityState.Detached;
            }
        }

        public async Task<bool> CanConnectAsync(CancellationToken ct = default)
        {
            return await context.Database.CanConnectAsync(ct);
        }
    }
}