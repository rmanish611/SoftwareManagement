using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// The single unit of work for the whole application (ADR-09: EF Core's DbContext already is
/// one, so no repository layer wraps it). Entity configurations are discovered from this
/// assembly, so a new module adds a configuration file and nothing else.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : DbContext(options)
{
    private readonly IClock _clock = clock;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditColumns();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditColumns();
        return base.SaveChanges();
    }

    /// <summary>
    /// Audit columns are stamped here rather than by each caller, so no write path can forget
    /// them (03-data-model.md, standard columns). The actor is filled in by the API layer in a
    /// later phase; until then the column carries the process identity rather than null.
    /// </summary>
    private void StampAuditColumns()
    {
        var now = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
                    {
                        entry.Entity.CreatedBy = "system";
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy ??= "system";
                    entry.Property(e => e.CreatedAtUtc).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
