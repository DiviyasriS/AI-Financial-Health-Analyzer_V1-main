using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Category> Categories { get; set; }

    public DbSet<Insight> Insights { get; set; }

    public DbSet<RiskPrediction> RiskPredictions { get; set; }
    public DbSet<AuthProvider> AuthProviders { get; set; }
    public DbSet<OtpRequest> OtpRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique email constraint — prevents race conditions that EmailExistsAsync can't catch
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.MobileNumber)
            .IsUnique();

        modelBuilder.Entity<AuthProvider>()
            .HasIndex(p => new { p.ProviderName, p.ProviderUserId })
            .IsUnique();

        modelBuilder.Entity<AuthProvider>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OtpRequest>()
            .HasIndex(o => new { o.MobileNumber, o.ExpiresAtUtc });

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