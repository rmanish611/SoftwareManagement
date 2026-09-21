using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Sales;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Quotes");
        builder.Property(q => q.QuoteNumber).HasMaxLength(24).IsRequired();
        builder.Property(q => q.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(q => q.Notes).HasMaxLength(2000);
        builder.Property(q => q.RejectReason).HasMaxLength(500);
        builder.Property(q => q.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(q => q.ModifiedBy).HasMaxLength(256);
        builder.Property(q => q.RowVersion).IsRowVersion();

        foreach (var money in new[] { nameof(Quote.SubTotal), nameof(Quote.DiscountTotal), nameof(Quote.TaxTotal), nameof(Quote.GrandTotal) })
        {
            builder.Property<decimal>(money).HasPrecision(18, 2);
        }

        // Unique, and the database says so rather than the application hoping. A duplicate quote
        // number is a question from an auditor nobody can answer (BR-SALE-01).
        builder.HasIndex(q => q.QuoteNumber).IsUnique();

        builder.HasIndex(q => new { q.OrganisationId, q.Status });

        builder.HasOne(q => q.Organisation).WithMany().HasForeignKey(q => q.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Contact).WithMany().HasForeignKey(q => q.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Lead).WithMany().HasForeignKey(q => q.LeadId).OnDelete(DeleteBehavior.SetNull);

        // The quote this one replaces. Restrict, because losing the original would leave a revision
        // claiming to revise nothing.
        builder.HasOne<Quote>().WithMany().HasForeignKey(q => q.RevisionOfQuoteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(q => q.Lines).WithOne(l => l.Quote!).HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class QuoteLineItemConfiguration : IEntityTypeConfiguration<QuoteLineItem>
{
    public void Configure(EntityTypeBuilder<QuoteLineItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("QuoteLineItems");
        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Property(l => l.DiscountAmount).HasPrecision(18, 2);
        builder.Property(l => l.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(l => l.LineTotal).HasPrecision(18, 2);
        builder.Property(l => l.TaxAmount).HasPrecision(18, 2);
        builder.Property(l => l.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(l => l.ModifiedBy).HasMaxLength(256);
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => new { l.QuoteId, l.SortOrder });

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Three arithmetic impossibilities, refused by the database rather than by every future
        // write path remembering to check (BR-SALE-03).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_QuoteLineItems_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_QuoteLineItems_UnitPrice", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_QuoteLineItems_Discount", "[DiscountAmount] >= 0 AND [DiscountAmount] <= [Quantity] * [UnitPrice]");
        });
    }
}

public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("NumberSequences");
        builder.Property(s => s.Key).HasMaxLength(30).IsRequired();
        builder.Property(s => s.FinancialYear).HasMaxLength(9).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One row per document kind per year, and the database enforces it: two rows for the same
        // sequence would hand out the same number twice.
        builder.HasIndex(s => new { s.Key, s.FinancialYear }).IsUnique();
    }
}
