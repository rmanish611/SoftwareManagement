using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Content;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Pages");
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.Status, p.PublishAtUtc });

        builder.HasOne(p => p.Seo).WithMany().HasForeignKey(p => p.SeoMetadataId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Sections).WithOne(s => s.Page!).HasForeignKey(s => s.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PageSectionConfiguration : IEntityTypeConfiguration<PageSection>
{
    public void Configure(EntityTypeBuilder<PageSection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PageSections");
        builder.Property(s => s.Heading).HasMaxLength(200);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // Two sections cannot claim the same position: an ambiguous order renders differently on
        // every request, which is the kind of bug nobody can reproduce.
        builder.HasIndex(s => new { s.PageId, s.SortOrder }).IsUnique();
        builder.HasOne(s => s.MediaAsset).WithMany().HasForeignKey(s => s.MediaAssetId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class SeoMetadataConfiguration : IEntityTypeConfiguration<SeoMetadata>
{
    public void Configure(EntityTypeBuilder<SeoMetadata> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("SeoMetadata");
        builder.Property(s => s.MetaTitle).HasMaxLength(120).IsRequired();
        builder.Property(s => s.MetaDescription).HasMaxLength(320);
        builder.Property(s => s.CanonicalUrl).HasMaxLength(500);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasOne(s => s.OgImage).WithMany().HasForeignKey(s => s.OgImageAssetId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Redirects");
        builder.Property(r => r.FromPath).HasMaxLength(400).IsRequired();
        builder.Property(r => r.ToPath).HasMaxLength(400).IsRequired();
        builder.Property(r => r.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(256);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.HasIndex(r => r.FromPath).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_Redirects_NotSelfReferencing", "[FromPath] <> [ToPath]"));
    }
}

public sealed class ContentVersionConfiguration : IEntityTypeConfiguration<ContentVersion>
{
    public void Configure(EntityTypeBuilder<ContentVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ContentVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(v => v.CreatedBy).HasMaxLength(256).IsRequired();
        builder.HasIndex(v => new { v.EntityType, v.EntityId, v.VersionNumber }).IsUnique();
    }
}

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("MediaAssets");
        builder.Property(m => m.FileName).HasMaxLength(260).IsRequired();
        builder.Property(m => m.StorageKey).HasMaxLength(400).IsRequired();
        builder.Property(m => m.WebStorageKey).HasMaxLength(400);
        builder.Property(m => m.ThumbnailStorageKey).HasMaxLength(400);
        builder.Property(m => m.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(m => m.AltText).HasMaxLength(300);
        builder.Property(m => m.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(m => m.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(256);
        builder.Property(m => m.RowVersion).IsRowVersion();
        builder.HasIndex(m => m.StorageKey).IsUnique();
        builder.HasIndex(m => m.Sha256);
        builder.ToTable(t => t.HasCheckConstraint("CK_MediaAssets_Size", "[SizeBytes] > 0 AND [SizeBytes] <= 26214400"));
    }
}

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Services");
        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Summary).HasMaxLength(400).IsRequired();
        builder.Property(s => s.IconKey).HasMaxLength(60);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasIndex(s => s.Slug).IsUnique();
    }
}

public sealed class TechnologyConfiguration : IEntityTypeConfiguration<Technology>
{
    public void Configure(EntityTypeBuilder<Technology> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Technologies");
        builder.Property(t => t.Name).HasMaxLength(80).IsRequired();
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasOne(t => t.Logo).WithMany().HasForeignKey(t => t.LogoAssetId).OnDelete(DeleteBehavior.SetNull);
        builder.ToTable(x => x.HasCheckConstraint("CK_Technologies_Proficiency", "[Proficiency] IS NULL OR ([Proficiency] >= 1 AND [Proficiency] <= 5)"));
    }
}

public sealed class ServiceTechnologyConfiguration : IEntityTypeConfiguration<ServiceTechnology>
{
    public void Configure(EntityTypeBuilder<ServiceTechnology> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ServiceTechnologies");
        builder.HasKey(st => new { st.ServiceId, st.TechnologyId });
        builder.HasOne(st => st.Service).WithMany(s => s!.ServiceTechnologies).HasForeignKey(st => st.ServiceId).OnDelete(DeleteBehavior.Cascade);

        // Deleting a technology that a service still lists would silently change what the site
        // claims the company works with, so it is refused (REQ-SITE-010).
        builder.HasOne(st => st.Technology).WithMany(t => t!.ServiceTechnologies).HasForeignKey(st => st.TechnologyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("TeamMembers");
        builder.Property(t => t.FullName).HasMaxLength(150).IsRequired();
        builder.Property(t => t.RoleTitle).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Bio).HasMaxLength(1000);
        builder.Property(t => t.LinkedInUrl).HasMaxLength(300);
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasOne(t => t.Photo).WithMany().HasForeignKey(t => t.PhotoAssetId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TestimonialConfiguration : IEntityTypeConfiguration<Testimonial>
{
    public void Configure(EntityTypeBuilder<Testimonial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Testimonials");
        builder.Property(t => t.AuthorName).HasMaxLength(120).IsRequired();
        builder.Property(t => t.AuthorRole).HasMaxLength(120).IsRequired();
        builder.Property(t => t.OrganisationName).HasMaxLength(200);
        builder.Property(t => t.Quote).HasMaxLength(1000).IsRequired();
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();
    }
}

public sealed class NavigationItemConfiguration : IEntityTypeConfiguration<NavigationItem>
{
    public void Configure(EntityTypeBuilder<NavigationItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("NavigationItems");
        builder.Property(n => n.Label).HasMaxLength(80).IsRequired();
        builder.Property(n => n.ExternalUrl).HasMaxLength(500);
        builder.Property(n => n.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(n => n.ModifiedBy).HasMaxLength(256);
        builder.Property(n => n.RowVersion).IsRowVersion();

        // A menu item points at a page or at an external URL, never at both and never at neither.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_NavigationItems_OneTarget",
            "([PageId] IS NOT NULL AND [ExternalUrl] IS NULL) OR ([PageId] IS NULL AND [ExternalUrl] IS NOT NULL)"));

        // Deleting a page that a menu still links to would leave a dead link in the header, so it
        // is refused (BR-SITE-08).
        builder.HasOne(n => n.Page).WithMany().HasForeignKey(n => n.PageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.Parent).WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Announcements");
        builder.Property(a => a.Message).HasMaxLength(300).IsRequired();
        builder.Property(a => a.LinkUrl).HasMaxLength(500);
        builder.Property(a => a.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(256);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.ToTable(t => t.HasCheckConstraint("CK_Announcements_Window", "[EndsAtUtc] > [StartsAtUtc]"));
    }
}
