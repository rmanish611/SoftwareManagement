using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Leads;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Leads");
        builder.Property(l => l.FullName).HasMaxLength(150).IsRequired();
        builder.Property(l => l.Email).HasMaxLength(256);
        builder.Property(l => l.Phone).HasMaxLength(20);
        builder.Property(l => l.CompanyName).HasMaxLength(200);
        builder.Property(l => l.Message).HasMaxLength(4000);
        builder.Property(l => l.DisqualifyReason).HasMaxLength(500);
        builder.Property(l => l.UtmJson).HasMaxLength(1000);
        builder.Property(l => l.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(l => l.ModifiedBy).HasMaxLength(256);
        builder.Property(l => l.RowVersion).IsRowVersion();

        // The lead inbox's default view, and the only index that matters for it: newest first
        // within a stage, ignoring deleted rows.
        builder.HasIndex(l => new { l.Stage, l.CreatedAtUtc })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Leads_Stage_Created");

        builder.HasIndex(l => l.Email);

        // The SLA sweep asks one question - what is overdue and still unanswered - and this is the
        // only index that keeps it off a table scan as the lead table grows (NFR-PERF-05).
        builder.HasIndex(l => l.SlaDueAtUtc)
            .HasFilter("[FirstResponseAtUtc] IS NULL")
            .HasDatabaseName("IX_Leads_SlaDue");

        builder.HasIndex(l => new { l.OwnerUserId, l.Stage }).HasDatabaseName("IX_Leads_Owner_Stage");

        // A lead nobody can reply to is not a lead. The database refuses one rather than trusting
        // every future write path to remember (BR-LEAD-01).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Leads_Contactable",
            "[Email] IS NOT NULL OR [Phone] IS NOT NULL"));

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(l => l.Submissions).WithOne(s => s.Lead!).HasForeignKey(s => s.LeadId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.Organisation).WithMany().HasForeignKey(l => l.OrganisationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(l => l.Contact).WithMany().HasForeignKey(l => l.ContactId).OnDelete(DeleteBehavior.SetNull);

        // The survivor of a merge, pointed at by the record that was merged away (BR-LEAD-08).
        // Restrict rather than cascade: deleting a lead must never silently take the conversation
        // that was folded into it.
        builder.HasOne<Lead>().WithMany().HasForeignKey(l => l.MergedIntoLeadId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Activities).WithOne(a => a.Lead!).HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.Cascade);

        // Soft-deleted leads disappear from every query without every query remembering to say so.
        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("FormDefinitions");
        builder.Property(f => f.Key).HasMaxLength(60).IsRequired();
        builder.Property(f => f.Title).HasMaxLength(150).IsRequired();
        builder.Property(f => f.Intro).HasMaxLength(600);
        builder.Property(f => f.SubmitLabel).HasMaxLength(60).IsRequired();
        builder.Property(f => f.SuccessMessage).HasMaxLength(600).IsRequired();
        builder.Property(f => f.ConsentText).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.NotifyEmails).HasMaxLength(500).IsRequired();
        builder.Property(f => f.AcknowledgementTemplateKey).HasMaxLength(80);
        builder.Property(f => f.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(f => f.ModifiedBy).HasMaxLength(256);
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.HasIndex(f => f.Key).IsUnique();
        builder.HasMany(f => f.Fields).WithOne(x => x.Form!).HasForeignKey(x => x.FormDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("FormFields");
        builder.Property(f => f.Name).HasMaxLength(60).IsRequired();
        builder.Property(f => f.Label).HasMaxLength(120).IsRequired();
        builder.Property(f => f.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(f => f.ModifiedBy).HasMaxLength(256);
        builder.Property(f => f.RowVersion).IsRowVersion();
        builder.HasIndex(f => new { f.FormDefinitionId, f.Name }).IsUnique();
    }
}

public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("FormSubmissions");
        builder.Property(s => s.MessageHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(s => s.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(s => s.UserAgent).HasMaxLength(400);
        builder.Property(s => s.Referrer).HasMaxLength(500);
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // The duplicate check and the rate limit are the two hot reads on this table, and both look
        // up recent rows by one column.
        builder.HasIndex(s => new { s.MessageHash, s.CreatedAtUtc });
        builder.HasIndex(s => new { s.IpAddress, s.CreatedAtUtc });

        builder.HasOne(s => s.Form).WithMany().HasForeignKey(s => s.FormDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Consent).WithOne(c => c!.Submission!).HasForeignKey<ConsentRecord>(c => c.FormSubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ConsentRecords");
        builder.Property(c => c.ConsentText).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.Purpose).HasMaxLength(300).IsRequired();
        builder.Property(c => c.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(256);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasIndex(c => c.FormSubmissionId).IsUnique();
    }
}
