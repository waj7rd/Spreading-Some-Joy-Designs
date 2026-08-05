using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.DAL.Context;

public class UnitOfWork : IUnitOfWork
{
    private readonly SpreadingJoyContext _context;

    public UnitOfWork(SpreadingJoyContext context)
    {
        _context = context;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        // Already inside one — join it rather than starting a second. Logic
        // classes call each other (accepting a request calls the ordering
        // rules), and nesting real transactions would throw.
        if (_context.Database.CurrentTransaction != null)
            return await operation();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var result = await operation();

        // A business refusal is reported as a result, not an exception, so
        // checking for a thrown exception alone would commit the very rows this
        // exists to prevent — the customer created just before the order was
        // turned down.
        if (result is IOperationResult { Success: false })
        {
            await transaction.RollbackAsync();
            return result;
        }

        await transaction.CommitAsync();
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
