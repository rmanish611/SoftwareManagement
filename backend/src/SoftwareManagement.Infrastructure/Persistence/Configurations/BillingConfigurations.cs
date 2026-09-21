using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Sales;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Tenants");
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.EnvironmentUrl).HasMaxLength(500);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();

        // One name per product per customer. Two "Production" tenants of the same product for one
        // company is a mistake somebody is about to make at two in the morning.
        builder.HasIndex(t => new { t.OrganisationId, t.ProductId, t.Name }).IsUnique();

        builder.HasOne(t => t.Organisation).WithMany().HasForeignKey(t => t.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Product).WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Subscriptions");
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(s => s.UnitPrice).HasPrecision(18, 2);
        builder.Property(s => s.CancelReason).HasMaxLength(500);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One subscription per tenant. Two would bill the same instance twice and no report could
        // say which was real.
        builder.HasIndex(s => s.TenantId).IsUnique();

        // The renewal sweep asks for subscriptions whose period ends soon; this is the index that
        // keeps it off a table scan.
        builder.HasIndex(s => new { s.Status, s.CurrentPeriodEndsOn });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Subscriptions_Seats", "[Seats] > 0");
            t.HasCheckConstraint("CK_Subscriptions_UnitPrice", "[UnitPrice] >= 0");
        });

        builder.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.PricingPlan).WithMany().HasForeignKey(s => s.PricingPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Quote).WithMany().HasForeignKey(s => s.QuoteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(s => s.Events).WithOne(e => e.Subscription!).HasForeignKey(e => e.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SubscriptionEventConfiguration : IEntityTypeConfiguration<SubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("SubscriptionEvents");
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.TriggeredBy).HasMaxLength(256).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ModifiedBy).HasMaxLength(256);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.SubscriptionId, e.EffectiveOn });
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Invoices");
        builder.Property(i => i.InvoiceNumber).HasMaxLength(24).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(i => i.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(256);
        builder.Property(i => i.RowVersion).IsRowVersion();

        foreach (var money in new[] { nameof(Invoice.SubTotal), nameof(Invoice.TaxTotal), nameof(Invoice.GrandTotal), nameof(Invoice.AmountPaid) })
        {
            builder.Property<decimal>(money).HasPrecision(18, 2);
        }

        // Gapless and unique, said by the database (BR-SALE-10).
        builder.HasIndex(i => i.InvoiceNumber).IsUnique();

        // The dunning sweep's question: what is unpaid and past its date.
        builder.HasIndex(i => new { i.Status, i.DueDate });

        // More received than billed is not a state this table holds. A refund is a separate row
        // with IsRefund set, and it lowers AmountPaid rather than raising it past the total.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Invoices_AmountPaid", "[AmountPaid] >= 0 AND [AmountPaid] <= [GrandTotal]"));

        builder.HasOne(i => i.Subscription).WithMany().HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(i => i.Organisation).WithMany().HasForeignKey(i => i.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Payments).WithOne(p => p.Invoice!).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Payments");
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // The same transfer cannot be entered twice by two people reconciling one statement
        // (EX-226).
        builder.HasIndex(p => new { p.InvoiceId, p.ReferenceNumber }).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"));
    }
}
