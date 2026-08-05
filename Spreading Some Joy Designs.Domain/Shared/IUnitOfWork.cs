namespace SpreadingJoy.Domain.Shared;

// Makes a business operation all-or-nothing.
//
// The repositories each expose SaveChangesAsync, and an operation touching
// several entities ends up calling it several times. They share one DbContext,
// so those aren't separate connections — but each save commits its own implicit
// transaction, which means a failure part-way through leaves the earlier writes
// committed.
//
// Accepting an order request is the clear case: it creates a customer, attaches
// the design to them, then creates the order and its lines. If the order is
// refused — the press is full that day, the artwork was never approved — the
// customer is already on file, from a request that was never accepted.
//
// Two ways an operation can fail, and both have to roll back:
//
//   - it throws, which disposes the transaction uncommitted
//   - it returns a result whose Success is false
//
// The second is the one that matters here. This codebase reports business
// failures with result objects rather than exceptions — "that day is fully
// booked" is an outcome, not an error — so a Unit of Work that only rolled back
// on exceptions would commit the orphans it exists to prevent.
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);

    Task ExecuteAsync(Func<Task> operation);
}

// Implemented by the result types so the Unit of Work can tell a refusal from a
// success without knowing which operation it wrapped.
public interface IOperationResult
{
    bool Success { get; }
}
