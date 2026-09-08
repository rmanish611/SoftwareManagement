using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Settings;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// The single unit of work for the whole application (ADR-09: EF Core's DbContext already is one,
/// so no repository layer wraps it). Entity configurations are discovered from this assembly, so a
/// new module adds a configuration file and nothing else.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock)
    : IdentityDbContext<AdminUser, AdminRole, Guid>(options)
{
    private readonly IClock _clock = clock;

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
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
    /// Audit columns are stamped here rather than by each caller, so no write path can forget them
    /// (03-data-model.md, standard columns). CreatedAtUtc and CreatedBy are pinned on update, so an
    /// edit can never rewrite who created a row.
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

        foreach (var entry in ChangeTracker.Entries<AdminUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
                {
                    entry.Entity.CreatedBy = "system";
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAtUtc = now;
                entry.Property(e => e.CreatedAtUtc).IsModified = false;
                entry.Property(e => e.CreatedBy).IsModified = false;
            }
        }
    }
}
