using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TorrentDrive.Core.Entities;

namespace TorrentDrive.Infrastructure.Interceptors
{
    public class UpdateAuditableEntitiesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            DbContext? context = eventData.Context;

            if (context is null)
            {
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Modified || entry.State == EntityState.Added)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
