using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Notifications;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("OutboxEmails");
        builder.Property(o => o.TemplateKey).HasMaxLength(80).IsRequired();
        builder.Property(o => o.ToAddress).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CcAddress).HasMaxLength(500);
        builder.Property(o => o.Subject).HasMaxLength(300).IsRequired();
        builder.Property(o => o.LastError).HasMaxLength(1000);
        builder.Property(o => o.RelatedEntityType).HasMaxLength(60);
        builder.Property(o => o.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(o => o.ModifiedBy).HasMaxLength(256);
        builder.Property(o => o.RowVersion).IsRowVersion();

        // The pump's only query: what is due, oldest first.
        builder.HasIndex(o => new { o.Status, o.NextAttemptAtUtc });

        builder.HasMany(o => o.DeliveryLogs).WithOne(l => l.OutboxEmail!).HasForeignKey(l => l.OutboxEmailId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmailDeliveryLogConfiguration : IEntityTypeConfiguration<EmailDeliveryLog>
{
    public void Configure(EntityTypeBuilder<EmailDeliveryLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("EmailDeliveryLogs");
        builder.Property(l => l.SmtpStatusCode).HasMaxLength(20);
        builder.Property(l => l.SmtpResponse).HasMaxLength(1000);
        builder.HasIndex(l => new { l.OutboxEmailId, l.AttemptNumber }).IsUnique();
    }
}

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("EmailTemplates");
        builder.Property(t => t.Key).HasMaxLength(80).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(300).IsRequired();
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.Key).IsUnique();
    }
}
