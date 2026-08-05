using System.Linq.Expressions;

namespace SpreadingJoy.Domain.IRepositories.IBase;

// The generic contract every repository gets for free. T is the entity type.
public interface IGenericRepository<T> where T : class
{
    IQueryable<T> GetAll();
    IQueryable<T> FindBy(Expression<Func<T, bool>> predicate);
    Task<IList<T>> FindByAsync(Expression<Func<T, bool>> predicate);
    Task<IList<T>> GetAllAsync();
    Task<T?> GetAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    void Add(T entity);
    void Delete(T entity);
    void Edit(T entity);
    void Save();
    Task SaveChangesAsync();
}
