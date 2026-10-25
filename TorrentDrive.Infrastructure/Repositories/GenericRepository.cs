using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Interfaces;
using TorrentDrive.Infrastructure.Data;

namespace TorrentDrive.Infrastructure.Repositories
{
    public class GenericRepository<T>(ApplicationDbContext context) : IGenericRepository<T> where T : BaseEntity
    {
        public async Task<T?> GetByIdAsync(int id)
        {
            return await context.Set<T>().FindAsync(id);
        }

        public async Task<IReadOnlyList<T>> ListAllAsync()
        {
            return await context.Set<T>().ToListAsync();
        }

        public async Task<T?> FirstOrDefaultAsync(
            Expression<Func<T, bool>> criteria,
            params Expression<Func<T, object?>>[] includes)
        {
            IQueryable<T> query = context.Set<T>();
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
            return await query.FirstOrDefaultAsync(criteria);
        }

        public async Task<IReadOnlyList<T>> ListAsync(
            Expression<Func<T, bool>>? criteria = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            int? skip = null,
            int? take = null,
            params Expression<Func<T, object?>>[] includes)
        {
            IQueryable<T> query = context.Set<T>();

            if (criteria != null)
            {
                query = query.Where(criteria);
            }

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            if (orderBy != null)
            {
                query = orderBy(query);
            }

            if (skip.HasValue)
            {
                query = query.Skip(skip.Value);
            }

            if (take.HasValue)
            {
                query = query.Take(take.Value);
            }

            return await query.ToListAsync();
        }

        public async Task<int> CountAsync(Expression<Func<T, bool>> criteria)
        {
            return await context.Set<T>().CountAsync(criteria);
        }

        public void Add(T entity)
        {
            context.Set<T>().Add(entity);
        }

        public void Update(T entity)
        {
            context.Set<T>().Attach(entity);
            context.Entry(entity).State = EntityState.Modified;
        }

        public void Delete(T entity)
        {
            context.Set<T>().Remove(entity);
        }
    }
}