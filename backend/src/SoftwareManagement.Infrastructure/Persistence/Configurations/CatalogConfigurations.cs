using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Catalog;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Products");
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Tagline).HasMaxLength(250).IsRequired();
        builder.Property(p => p.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.DocsUrl).HasMaxLength(500);
        builder.Property(p => p.RepositoryUrl).HasMaxLength(500);
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.Status, p.CategoryId, p.SortOrder });

        builder.HasOne(p => p.Category).WithMany(c => c!.Products).HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Hero).WithMany().HasForeignKey(p => p.HeroAssetId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Seo).WithMany().HasForeignKey(p => p.SeoMetadataId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Demo).WithOne(d => d!.Product!).HasForeignKey<DemoEnvironment>(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Features).WithOne(f => f.Product!).HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Screenshots).WithOne(s => s.Product!).HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Plans).WithOne(pl => pl.Product!).HasForeignKey(pl => pl.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Faqs).WithOne(f => f.Product!).HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ProductCategories");
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.IconKey).HasMaxLength(60);
        builder.Property(c => c.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(256);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

public sealed class ProductFeatureConfiguration : IEntityTypeConfiguration<ProductFeature>
{
    public void Configure(EntityTypeBuilder<ProductFeature> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ProductFeatures");
        builder.Property(f => f.Name).HasMaxLength(150).IsRequired();
        builder.Property(f => f.Description).HasMaxLength(600);
        builder.Property(f => f.GroupName).HasMaxLength(80);
        builder.Property(f => f.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(f => f.ModifiedBy).HasMaxLength(256);
        builder.Property(f => f.RowVersion).IsRowVersion();

        // Two features with the same name on one product is a copy-and-paste mistake, and it makes
        // the plan comparison table ambiguous.
        builder.HasIndex(f => new { f.ProductId, f.Name }).IsUnique();
    }
}

public sealed class ProductScreenshotConfiguration : IEntityTypeConfiguration<ProductScreenshot>
{
    public void Configure(EntityTypeBuilder<ProductScreenshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ProductScreenshots");
        builder.Property(s => s.Caption).HasMaxLength(200);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasIndex(s => new { s.ProductId, s.SortOrder }).IsUnique();

        // A screenshot whose file was deleted is dropped from the gallery rather than rendered as
        // a broken image (REQ-CAT-004).
        builder.HasOne(s => s.MediaAsset).WithMany().HasForeignKey(s => s.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
{
    public void Configure(EntityTypeBuilder<PricingPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PricingPlans");
        builder.Property(p => p.Name).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.SetupFee).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // At most one recommended plan per product, enforced by the database rather than by hoping
        // every write path remembers (BR-CAT-03).
        builder.HasIndex(p => p.ProductId)
            .IsUnique()
            .HasFilter("[IsRecommended] = 1")
            .HasDatabaseName("UX_PricingPlans_OneRecommendedPerProduct");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PricingPlans_Price",
            "[Price] >= 0 AND ([Price] > 0 OR [IsFreeTier] = 1)"));

        builder.ToTable(t => t.HasCheckConstraint("CK_PricingPlans_Seats", "[IncludedSeats] > 0"));
    }
}

public sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PlanFeatures");
        builder.Property(f => f.LimitValue).HasMaxLength(60);
        builder.Property(f => f.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(f => f.ModifiedBy).HasMaxLength(256);
        builder.Property(f => f.RowVersion).IsRowVersion();
        builder.HasIndex(f => new { f.PricingPlanId, f.ProductFeatureId }).IsUnique();

        builder.HasOne(f => f.PricingPlan).WithMany(p => p!.PlanFeatures).HasForeignKey(f => f.PricingPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.ProductFeature).WithMany(p => p!.PlanFeatures).HasForeignKey(f => f.ProductFeatureId).OnDelete(DeleteBehavior.Restrict);

        // "Limited" without a number tells a buyer nothing.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PlanFeatures_LimitValue",
            "[Availability] <> 1 OR ([LimitValue] IS NOT NULL AND LEN([LimitValue]) > 0)"));
    }
}

public sealed class DemoEnvironmentConfiguration : IEntityTypeConfiguration<DemoEnvironment>
{
    public void Configure(EntityTypeBuilder<DemoEnvironment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("DemoEnvironments");
        builder.Property(d => d.Url).HasMaxLength(500).IsRequired();
        builder.Property(d => d.DemoUsername).HasMaxLength(120);
        builder.Property(d => d.DemoPassword).HasMaxLength(120);
        builder.Property(d => d.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(d => d.ModifiedBy).HasMaxLength(256);
        builder.Property(d => d.RowVersion).IsRowVersion();
        builder.HasIndex(d => d.ProductId).IsUnique();
    }
}

public sealed class FaqItemConfiguration : IEntityTypeConfiguration<FaqItem>
{
    public void Configure(EntityTypeBuilder<FaqItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("FaqItems");
        builder.Property(f => f.Question).HasMaxLength(300).IsRequired();
        builder.Property(f => f.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(f => f.ModifiedBy).HasMaxLength(256);
        builder.Property(f => f.RowVersion).IsRowVersion();
        builder.HasIndex(f => new { f.ProductId, f.SortOrder });
    }
}
