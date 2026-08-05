using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace SpreadingJoy.DAL.Context;

// Lets the context configure itself from appsettings when it wasn't built
// through dependency injection — EF Core tooling does that, and so does
// anything constructing it directly.
//
// When it comes from DI the options are already set, and the IsConfigured guard
// skips all of this.
public partial class SpreadingJoyContext
{
    public SpreadingJoyContext()
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .Build();

            var connectionString = configuration.GetConnectionString("SpreadingJoyContext");
            optionsBuilder.UseSqlServer(connectionString);
        }
    }
}
