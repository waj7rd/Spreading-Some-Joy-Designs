using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.DAL.Repositories.Base;

// The single implementation of IGenericRepository, shared by every entity repo.
// C is the context type; T is the entity type.
//
// The context is injected, never constructed here. Constructing it would give
// every repository its own context on its own connection — so a request
// touching customers, designs and orders would open three of them, an entity
// loaded by one repository would be invisible to the others, and no transaction
// could ever span two repositories. That last one makes the Unit of Work
// silently useless: it opens a transaction on a context nobody else writes
// through, so nothing rolls back.
//
// With one scoped context per request, SaveChangesAsync on any repository
// writes through the same connection, and IUnitOfWork can wrap the lot.
public abstract class GenericRepository<C, T> : IGenericRepository<T>
    where T : class
    where C : SpreadingJoyContext
{
    private readonly C _entities;

    protected GenericRepository(C context)
    {
        _entities = context;
    }

    public C Context => _entities;

    #region ASYNCHRONOUS
    public virtual async Task<IList<T>> GetAllAsync()
    {
        return await _entities.Set<T>().ToListAsync();
    }
    public virtual async Task AddAsync(T entity)
    {
        await _entities.Set<T>().AddAsync(entity);
    }
    public virtual async Task SaveChangesAsync()
    {
        await _entities.SaveChangesAsync();
    }
    public virtual async Task<IList<T>> FindByAsync(Expression<Func<T, bool>> predicate)
    {
        return await _entities.Set<T>().Where(predicate).ToListAsync();
    }
    public virtual async Task<T?> GetAsync(Expression<Func<T, bool>> predicate)
    {
        return await _entities.Set<T>().FirstOrDefaultAsync(predicate);
    }
    #endregion

    #region SYNCHRONOUS
    public virtual IQueryable<T> GetAll()
    {
        return _entities.Set<T>();
    }
    public IQueryable<T> FindBy(Expression<Func<T, bool>> predicate)
    {
        return _entities.Set<T>().Where(predicate);
    }
    public virtual void Add(T entity)
    {
        _entities.Set<T>().Add(entity);
    }
    public virtual void Delete(T entity)
    {
        _entities.Set<T>().Remove(entity);
    }
    public virtual void Edit(T entity)
    {
        _entities.Entry(entity).State = EntityState.Modified;
    }
    public virtual void Save()
    {
        _entities.SaveChanges();
    }
    #endregion
}
