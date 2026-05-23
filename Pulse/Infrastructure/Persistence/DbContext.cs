using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pulse");

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);

            entity.Property(n => n.Recipient)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(n => n.Subject)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(n => n.Body)
                .IsRequired();

            entity.Property(n => n.Type)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(n => n.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(n => n.CreatedAt)
                .HasColumnType("datetime2")
                .IsRequired();

            entity.Property(n => n.ProcessedAt)
                .HasColumnType("datetime2");

            entity.Property(n => n.ErrorMessage)
                .HasMaxLength(2048);

            entity.Property(n => n.IsDispatched)
                .IsRequired()
                .HasDefaultValue(false);

            entity.HasIndex(n => new { n.Status, n.CreatedAt })
                .HasDatabaseName("IX_Notifications_Status_CreatedAt");

            entity.HasIndex(n => n.IsDispatched)
                .HasDatabaseName("IX_Notifications_IsDispatched")
                .HasFilter("[IsDispatched] = 0");
        });
    }
}

public class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException(
                "Set the ConnectionStrings__Default environment variable before running EF migrations.");

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new NotificationDbContext(options);
    }
}
