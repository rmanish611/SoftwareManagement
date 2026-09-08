using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Analytics;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class PageViewStatConfiguration : IEntityTypeConfiguration<PageViewStat>
{
    public void Configure(EntityTypeBuilder<PageViewStat> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PageViewStats");
        builder.Property(s => s.Path).HasMaxLength(400).IsRequired();
        builder.Property(s => s.StatDate).HasColumnType("date");
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One row per path per day, enforced by the database. The counter increments with an atomic
        // UPDATE and falls back to an insert; without this index two concurrent first-views of the
        // same page would create two rows and the day's total would be split between them.
        builder.HasIndex(s => new { s.StatDate, s.Path })
            .IsUnique()
            .HasDatabaseName("IX_PageViewStats_Date");
    }
}
