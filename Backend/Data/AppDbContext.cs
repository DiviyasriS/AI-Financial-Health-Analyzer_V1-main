using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Category> Categories { get; set; }

    public DbSet<Insight> Insights { get; set; }

    public DbSet<RiskPrediction> RiskPredictions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique email constraint — prevents race conditions that EmailExistsAsync can't catch
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Correct decimal precision for currency
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Amount)
            .HasPrecision(18, 2);

        // Prevent lazy-loading surprises
        modelBuilder.Entity<User>()
            .Navigation(u => u.Transactions)
            .AutoInclude(false);
    }
}