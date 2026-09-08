using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Analytics;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Content;
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

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<PageSection> PageSections => Set<PageSection>();

    public DbSet<SeoMetadata> SeoMetadata => Set<SeoMetadata>();

    public DbSet<Redirect> Redirects => Set<Redirect>();

    public DbSet<ContentVersion> ContentVersions => Set<ContentVersion>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<Technology> Technologies => Set<Technology>();

    public DbSet<ServiceTechnology> ServiceTechnologies => Set<ServiceTechnology>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<Testimonial> Testimonials => Set<Testimonial>();

    public DbSet<NavigationItem> NavigationItems => Set<NavigationItem>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<ProductFeature> ProductFeatures => Set<ProductFeature>();

    public DbSet<ProductScreenshot> ProductScreenshots => Set<ProductScreenshot>();

    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();

    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();

    public DbSet<DemoEnvironment> DemoEnvironments => Set<DemoEnvironment>();

    public DbSet<FaqItem> FaqItems => Set<FaqItem>();

    public DbSet<PageViewStat> PageViewStats => Set<PageViewStat>();

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
