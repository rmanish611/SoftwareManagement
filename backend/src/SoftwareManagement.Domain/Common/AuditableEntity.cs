namespace SoftwareManagement.Domain.Common;

/// <summary>
/// Base for every persisted entity. The audit columns and the concurrency token are declared
/// once here so no entity can be added without them (03-data-model.md, standard columns).
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>
    /// SQL Server rowversion. A concurrent second writer is rejected with 409 rather than
    /// silently overwriting the first (EX-101, NFR-CONC-02).
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    public bool IsTransient() => Id == Guid.Empty;
}
