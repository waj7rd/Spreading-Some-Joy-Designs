using Microsoft.EntityFrameworkCore;
using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.DAL.Context;

public partial class SpreadingJoyContext : DbContext
{
    public SpreadingJoyContext(DbContextOptions<SpreadingJoyContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Studio> Studios { get; set; } = null!;

    public virtual DbSet<Product> Products { get; set; } = null!;

    public virtual DbSet<Artwork> Artworks { get; set; } = null!;

    public virtual DbSet<Design> Designs { get; set; } = null!;

    public virtual DbSet<Customer> Customers { get; set; } = null!;

    public virtual DbSet<Order> Orders { get; set; } = null!;

    public virtual DbSet<OrderLine> OrderLines { get; set; } = null!;

    public virtual DbSet<OrderRequest> OrderRequests { get; set; } = null!;

    public virtual DbSet<User> Users { get; set; } = null!;

    public virtual DbSet<LoginAudit> LoginAudits { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
    }
}
