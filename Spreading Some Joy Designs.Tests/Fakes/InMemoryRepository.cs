using System.Linq.Expressions;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Tests.Fakes;

// In-memory stand-in for the generic repository. The Domain only ever sees the
// interfaces, so this is enough to exercise every business rule without a
// database — and without a mocking library, which keeps the tests readable.
public class InMemoryRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly List<T> Items = new();

    // Assigns the next identity value, mimicking what the database would do on
    // insert. Business logic reads the id back after saving.
    private readonly Action<T, int>? _assignId;
    private int _nextId = 1;

    public InMemoryRepository(Action<T, int>? assignId = null) => _assignId = assignId;

    public int SaveCount { get; private set; }

    public IReadOnlyList<T> All => Items;

    // Seeds without touching identity — for rows that already "exist".
    public InMemoryRepository<T> Seed(params T[] items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
            _nextId++;
        }

        return this;
    }

    public IQueryable<T> GetAll() => Items.AsQueryable();

    public IQueryable<T> FindBy(Expression<Func<T, bool>> predicate) =>
        Items.AsQueryable().Where(predicate);

    public Task<IList<T>> FindByAsync(Expression<Func<T, bool>> predicate) =>
        Task.FromResult<IList<T>>(Items.AsQueryable().Where(predicate).ToList());

    public Task<IList<T>> GetAllAsync() => Task.FromResult<IList<T>>(Items.ToList());

    public Task<T?> GetAsync(Expression<Func<T, bool>> predicate) =>
        Task.FromResult(Items.AsQueryable().FirstOrDefault(predicate));

    public Task AddAsync(T entity)
    {
        Add(entity);
        return Task.CompletedTask;
    }

    public void Add(T entity)
    {
        _assignId?.Invoke(entity, _nextId++);
        Items.Add(entity);
    }

    public void Delete(T entity) => Items.Remove(entity);

    public void Edit(T entity) { }

    public void Save() => SaveCount++;

    public Task SaveChangesAsync()
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public class FakeProductRepository : InMemoryRepository<Product>, IProductRepository
{
    public FakeProductRepository() : base((p, id) => p.ProductId = id) { }
}

public class FakeStudioRepository : InMemoryRepository<Studio>, IStudioRepository
{
    public FakeStudioRepository() : base((s, id) => s.StudioId = id) { }
}

public class FakeArtworkRepository : InMemoryRepository<Artwork>, IArtworkRepository
{
    public FakeArtworkRepository() : base((a, id) => a.ArtworkId = id) { }

    public Task<IList<Artwork>> GetByStatusAsync(string status) =>
        Task.FromResult<IList<Artwork>>(Items
            .Where(a => a.Status == status)
            .OrderBy(a => a.CreatedAt)
            .ToList());

    public Task<Artwork?> GetByStoredFileNameAsync(string storedFileName) =>
        Task.FromResult(Items.FirstOrDefault(a => a.StoredFileName == storedFileName));
}

public class FakeDesignRepository : InMemoryRepository<Design>, IDesignRepository
{
    public FakeDesignRepository() : base((d, id) => d.DesignId = id) { }

    // The real repository Includes the product and both artworks. The fake
    // relies on the test having wired the navigation properties itself, which
    // is what makes a test that forgets to look like the production path.
    public Task<Design?> GetWithArtworkAsync(int designId) =>
        Task.FromResult(Items.FirstOrDefault(d => d.DesignId == designId));

    public Task<Design?> GetByPublicTokenAsync(Guid publicToken) =>
        Task.FromResult(Items.FirstOrDefault(d => d.PublicToken == publicToken));

    public Task<IList<Design>> GetStudioDesignsAsync(bool activeOnly)
    {
        var designs = Items.Where(d => d.IsStudioDesign);

        // Mirrors the real query: the shop must never offer a design built on
        // an archived garment.
        if (activeOnly)
            designs = designs.Where(d => d.IsActive && (d.Product == null || d.Product.IsActive));

        return Task.FromResult<IList<Design>>(designs
            .OrderByDescending(d => d.IsActive)
            .ThenByDescending(d => d.CreatedAt)
            .ToList());
    }
}

public class FakeCustomerRepository : InMemoryRepository<Customer>, ICustomerRepository
{
    public FakeCustomerRepository() : base((c, id) => c.CustomerId = id) { }

    public Task<IList<Customer>> GetAllWithOrdersAsync() =>
        Task.FromResult<IList<Customer>>(Items.ToList());

    public Task<Customer?> GetWithOrdersAsync(int customerId) =>
        Task.FromResult(Items.FirstOrDefault(c => c.CustomerId == customerId));
}

public class FakeOrderRepository : InMemoryRepository<Order>, IOrderRepository
{
    public FakeOrderRepository() : base((o, id) => o.OrderId = id) { }

    public Task<Order?> GetWithLinesAsync(int orderId) =>
        Task.FromResult(Items.FirstOrDefault(o => o.OrderId == orderId));

    public Task<IList<Order>> GetDueOnAsync(DateTime date) =>
        Task.FromResult<IList<Order>>(Items.Where(o => o.DueOn.Date == date.Date).ToList());

    public Task<IList<Order>> GetOpenAsync() =>
        Task.FromResult<IList<Order>>(Items
            .Where(o => OrderStatus.IsOpen(o.Status))
            .OrderBy(o => o.DueOn)
            .ToList());
}

public class FakeOrderRequestRepository : InMemoryRepository<OrderRequest>, IOrderRequestRepository
{
    public FakeOrderRequestRepository() : base((r, id) => r.OrderRequestId = id) { }

    public Task<IList<OrderRequest>> GetByStatusAsync(string status) =>
        Task.FromResult<IList<OrderRequest>>(Items
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .ToList());

    public Task<OrderRequest?> GetWithDesignAsync(int orderRequestId) =>
        Task.FromResult(Items.FirstOrDefault(r => r.OrderRequestId == orderRequestId));
}

public class FakeUserRepository : InMemoryRepository<User>, IUserRepository
{
    public FakeUserRepository() : base((u, id) => u.UserId = id) { }

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Email == email));
}

public class FakeLoginAuditRepository : InMemoryRepository<LoginAudit>, ILoginAuditRepository
{
    public FakeLoginAuditRepository() : base((l, id) => l.LoginAuditId = id) { }

    public Task<IList<LoginAudit>> GetRecentAsync(int count) =>
        Task.FromResult<IList<LoginAudit>>(Items
            .OrderByDescending(l => l.OccurredAt)
            .Take(count)
            .ToList());
}

// Runs the operation and reports whether it would have been rolled back, so a
// test can assert that a refusal doesn't leave a customer behind.
public class FakeUnitOfWork : IUnitOfWork
{
    public int Executions { get; private set; }
    public bool RolledBack { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        Executions++;
        var result = await operation();

        if (result is IOperationResult { Success: false })
            RolledBack = true;

        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation)
    {
        await ExecuteAsync<object?>(async () =>
        {
            await operation();
            return null;
        });
    }
}
