using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Category> Categories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Email)
                  .HasMaxLength(256)
                  .IsRequired();

            entity.HasIndex(u => u.Email)
                  .IsUnique();

            entity.Property(u => u.PasswordHash)
                  .HasMaxLength(512)
                  .IsRequired();
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(t => t.Amount)
                  .HasColumnType("decimal(18,2)");

            entity.Property(t => t.Description)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.Property(t => t.Category)
                  .HasMaxLength(100);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name)
                  .HasMaxLength(100)
                  .IsRequired();
        });
    }
}