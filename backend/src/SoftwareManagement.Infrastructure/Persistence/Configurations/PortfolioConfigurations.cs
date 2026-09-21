using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Portfolio;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Projects");
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ClientDisplayName).HasMaxLength(150);
        builder.Property(p => p.Industry).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.Status, p.CompletedOn });

        // A project that finished before it started is a typo, and the database is where a typo
        // stops rather than where it is discovered later in a report.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Projects_Dates", "[CompletedOn] IS NULL OR [CompletedOn] >= [StartedOn]"));

        builder.HasOne(p => p.Organisation).WithMany().HasForeignKey(p => p.OrganisationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.ClientLogo).WithMany().HasForeignKey(p => p.ClientLogoId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.CaseStudy).WithOne(c => c.Project!).HasForeignKey<CaseStudy>(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CaseStudyConfiguration : IEntityTypeConfiguration<CaseStudy>
{
    public void Configure(EntityTypeBuilder<CaseStudy> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CaseStudies");
        builder.Property(c => c.Problem).IsRequired();
        builder.Property(c => c.Approach).IsRequired();
        builder.Property(c => c.Outcome).IsRequired();
        builder.Property(c => c.MetricsJson).IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(256);
        builder.Property(c => c.RowVersion).IsRowVersion();

        // One case study per project: two stories about the same delivery is two versions of the
        // truth.
        builder.HasIndex(c => c.ProjectId).IsUnique();

        builder.HasOne(c => c.Testimonial).WithMany().HasForeignKey(c => c.TestimonialId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ClientLogoConfiguration : IEntityTypeConfiguration<ClientLogo>
{
    public void Configure(EntityTypeBuilder<ClientLogo> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ClientLogos");
        builder.Property(l => l.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(l => l.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(l => l.ModifiedBy).HasMaxLength(256);
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => l.SortOrder);

        builder.HasOne(l => l.Organisation).WithMany().HasForeignKey(l => l.OrganisationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(l => l.MediaAsset).WithMany().HasForeignKey(l => l.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ApiCatalogEntryConfiguration : IEntityTypeConfiguration<ApiCatalogEntry>
{
    public void Configure(EntityTypeBuilder<ApiCatalogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ApiCatalogEntries");
        builder.Property(a => a.Name).HasMaxLength(150).IsRequired();
        builder.Property(a => a.Slug).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Purpose).HasMaxLength(600).IsRequired();
        builder.Property(a => a.BaseUrl).HasMaxLength(500);
        builder.Property(a => a.DocsUrl).HasMaxLength(500);
        builder.Property(a => a.OpenApiUrl).HasMaxLength(500);
        builder.Property(a => a.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(256);
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.Slug).IsUnique();

        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(a => a.Versions).WithOne(v => v.ApiCatalogEntry!).HasForeignKey(v => v.ApiCatalogEntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApiVersionConfiguration : IEntityTypeConfiguration<ApiVersion>
{
    public void Configure(EntityTypeBuilder<ApiVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ApiVersions");
        builder.Property(v => v.VersionLabel).HasMaxLength(30).IsRequired();
        builder.Property(v => v.ChangelogUrl).HasMaxLength(500);
        builder.Property(v => v.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(v => v.ModifiedBy).HasMaxLength(256);
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.ApiCatalogEntryId, v.VersionLabel }).IsUnique();

        // Exactly one current version per entry, enforced by a filtered unique index rather than by
        // every write path remembering to clear the old one first (BR-API-02).
        builder.HasIndex(v => new { v.ApiCatalogEntryId, v.IsCurrent })
            .IsUnique()
            .HasFilter("[IsCurrent] = 1")
            .HasDatabaseName("IX_ApiVersions_OneCurrent");

        // A deprecation with no sunset date tells a developer it is going away and not when, which
        // is the least useful thing it could say (BR-API-03).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ApiVersions_DeprecatedHasSunset", "[Status] <> 2 OR [SunsetDate] IS NOT NULL"));
    }
}

public sealed class ProjectTechnologyConfiguration : IEntityTypeConfiguration<ProjectTechnology>
{
    public void Configure(EntityTypeBuilder<ProjectTechnology> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ProjectTechnologies");
        builder.HasKey(pt => new { pt.ProjectId, pt.TechnologyId });

        builder.HasOne(pt => pt.Project!).WithMany(p => p.Technologies)
            .HasForeignKey(pt => pt.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // A technology is removed from the list, not from history: deleting one that a project used
        // would quietly rewrite what that project was built with.
        builder.HasOne(pt => pt.Technology!).WithMany()
            .HasForeignKey(pt => pt.TechnologyId).OnDelete(DeleteBehavior.Restrict);
    }
}
