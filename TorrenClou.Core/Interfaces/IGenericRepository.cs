using System.Linq.Expressions;
using TorrenClou.Core.Entities;

namespace TorrenClou.Core.Interfaces
{
    public interface IGenericRepository<T> where T : BaseEntity
    {
        Task<T?> GetByIdAsync(int id);
        Task<IReadOnlyList<T>> ListAllAsync();
        Task<T?> FirstOrDefaultAsync(
            Expression<Func<T, bool>> criteria,
            params Expression<Func<T, object?>>[] includes);
        Task<IReadOnlyList<T>> ListAsync(
            Expression<Func<T, bool>>? criteria = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            int? skip = null,
            int? take = null,
            params Expression<Func<T, object?>>[] includes);
        Task<int> CountAsync(Expression<Func<T, bool>> criteria);
        void Add(T entity);
        void Update(T entity);
        void Delete(T entity);
    }
}