using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.DAL.Context;

// Caches the studio record for the lifetime of the application.
//
// Registered as a singleton, so it can't hold a scoped DbContext — it takes the
// scope factory and opens a short-lived scope whenever it actually needs to
// read. Injecting the context directly here is the classic captive-dependency
// bug: a request-scoped context living forever inside a singleton, handing out
// stale entities and eventually throwing on a disposed connection.
public class StudioContextProvider : IStudioContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _gate = new();

    private Studio? _cached;

    public StudioContextProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Studio Current
    {
        get
        {
            // Double-checked so the common path — every request after the first
            // — doesn't take a lock to read a field that never changes.
            if (_cached != null)
                return _cached;

            lock (_gate)
            {
                _cached ??= Load();
                return _cached;
            }
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private Studio Load()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SpreadingJoyContext>();

        // AsNoTracking because this entity outlives the context that loaded it.
        // A tracked entity held by a singleton keeps its DbContext alive with it.
        var studio = context.Studios
            .AsNoTracking()
            .OrderBy(s => s.StudioId)
            .FirstOrDefault();

        // Failing loudly here beats every downstream rule quietly reading zeros:
        // a capacity of 0 and a turnaround of 0 would refuse every order with a
        // message that makes no sense.
        return studio ?? throw new InvalidOperationException(
            "No row in Studios. Run Scripts/CreateDatabase.sql and Scripts/SeedData.sql before starting the application.");
    }
}
