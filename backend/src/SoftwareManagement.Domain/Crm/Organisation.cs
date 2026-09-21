using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Crm;

/// <summary>
/// A customer company (E-32).
///
/// It arrives in this phase because converting a qualified lead has to put the customer somewhere
/// (REQ-LEAD-015), and a conversion that invented its own holding table would have to be undone in
/// P09. What P08 uses is the identity of the company and the fact that it exists; the GSTIN rules,
/// the addresses, the status lifecycle and the screens for all of it belong to P09.
/// </summary>
public class Organisation : AuditableEntity
{
    public string LegalName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Gstin { get; set; }

    public string? Website { get; set; }

    public string? Industry { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? PostalCode { get; set; }

    public string Country { get; set; } = "IN";

    public OrganisationStatus Status { get; set; } = OrganisationStatus.Prospect;

    public bool IsDeleted { get; set; }

    public ICollection<Contact> Contacts { get; } = [];
}

public enum OrganisationStatus
{
    Prospect = 0,
    Customer = 1,
    Inactive = 2,
}

/// <summary>
/// A person at a customer company (E-33). One email per organisation, because two rows for the
/// same person is how two people end up ringing them about the same thing (BR-CUST-02).
/// </summary>
public class Contact : AuditableEntity
{
    public Guid OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? JobTitle { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;
}
