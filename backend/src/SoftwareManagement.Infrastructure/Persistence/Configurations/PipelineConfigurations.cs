using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Domain.Leads;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class LeadActivityConfiguration : IEntityTypeConfiguration<LeadActivity>
{
    public void Configure(EntityTypeBuilder<LeadActivity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("LeadActivities");
        builder.Property(a => a.Body).HasMaxLength(4000);
        builder.Property(a => a.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(256);
        builder.Property(a => a.RowVersion).IsRowVersion();

        // The timeline: one lead, newest first. It is the query the detail screen runs every time
        // it opens, and the only one this table serves in anger.
        builder.HasIndex(a => new { a.LeadId, a.OccurredAtUtc }).HasDatabaseName("IX_LeadActivities_Lead");

        // Reminders that are due and not yet done. The sweep runs every few minutes and must not
        // read the whole history of every lead to find the handful it owes.
        builder.HasIndex(a => a.DueAtUtc)
            .HasFilter("[DueAtUtc] IS NOT NULL AND [IsCompleted] = 0")
            .HasDatabaseName("IX_LeadActivities_Due");

        // A note recording a stage change must say which stages, and one recording nothing at all
        // is a row that means nothing. The database refuses both rather than trusting every future
        // write path to remember.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LeadActivities_StageChangeHasStages",
            "[ActivityType] <> 5 OR ([FromStage] IS NOT NULL AND [ToStage] IS NOT NULL)"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LeadActivities_HasContent",
            "[Body] IS NOT NULL OR [ActivityType] IN (5, 6)"));
    }
}

public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Holidays");
        builder.Property(h => h.Name).HasMaxLength(120).IsRequired();
        builder.Property(h => h.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(h => h.ModifiedBy).HasMaxLength(256);
        builder.Property(h => h.RowVersion).IsRowVersion();

        // The same day twice would close the office twice, which is harmless, and would make the
        // "already exists" answer REQ-ADM-002 asks for impossible to give, which is not.
        builder.HasIndex(h => h.HolidayDate).IsUnique();
    }
}

public sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Organisations");
        builder.Property(o => o.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Gstin).HasMaxLength(15).IsFixedLength();
        builder.Property(o => o.Website).HasMaxLength(300);
        builder.Property(o => o.Industry).HasMaxLength(80);
        builder.Property(o => o.AddressLine1).HasMaxLength(200);
        builder.Property(o => o.AddressLine2).HasMaxLength(200);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.State).HasMaxLength(100);
        builder.Property(o => o.PostalCode).HasMaxLength(12);
        builder.Property(o => o.Country).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(o => o.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(o => o.ModifiedBy).HasMaxLength(256);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasIndex(o => o.DisplayName);
        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Contacts");
        builder.Property(c => c.FullName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.JobTitle).HasMaxLength(120);
        builder.Property(c => c.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(256);
        builder.Property(c => c.RowVersion).IsRowVersion();

        // One email per company. Two rows for the same person is how two people end up ringing
        // them about the same thing (BR-CUST-02).
        builder.HasIndex(c => new { c.OrganisationId, c.Email }).IsUnique();

        // Restrict, not cascade. A lead points at both the company and the person, so cascading
        // from the company would give SQL Server two paths to the same Leads row and it refuses the
        // schema outright. It is also the right behaviour on its own terms: an organisation is
        // retired by its IsDeleted flag, never removed, so a cascade here would only ever fire for
        // a hard delete nothing in the application performs.
        builder.HasOne(c => c.Organisation).WithMany(o => o.Contacts)
            .HasForeignKey(c => c.OrganisationId).OnDelete(DeleteBehavior.Restrict);

        // Matches the organisation's own filter. Without it EF warns, correctly, that a contact
        // would still be returned for a company that has been removed.
        builder.HasQueryFilter(c => !c.Organisation!.IsDeleted);
    }
}
