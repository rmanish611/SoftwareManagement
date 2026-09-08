using SoftwareManagement.Domain.Identity;

namespace SoftwareManagement.Application.Security;

/// <summary>
/// Which permissions each role holds, transcribed from the authorization matrix in
/// `docs/blueprint/06-authz.md`. This is the seed source and the thing the matrix test asserts
/// against the real endpoints, so the table in the blueprint and the behaviour of the API cannot
/// drift apart without a test failing.
///
/// The principles, restated so they are visible where the mapping lives:
/// the Auditor writes nothing; the Editor never sees personal data, money or settings; only the
/// Owner touches users, settings, refunds, cancellations and privacy operations.
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] EditorPermissions =
    [
        Permissions.Content.PageRead, Permissions.Content.PageWrite, Permissions.Content.PagePublish,
        Permissions.Content.VersionRestore, Permissions.Content.MediaRead, Permissions.Content.MediaWrite,
        Permissions.Content.MediaDelete, Permissions.Content.NavigationWrite,
        Permissions.Catalog.ProductRead, Permissions.Catalog.ProductWrite, Permissions.Catalog.ProductPublish,
        Permissions.Catalog.ProductArchive, Permissions.Catalog.PlanRead, Permissions.Catalog.PlanWrite,
        Permissions.Catalog.DemoCredentials, Permissions.Catalog.ApiRead, Permissions.Catalog.ApiWrite,
        Permissions.Portfolio.ProjectRead, Permissions.Portfolio.ProjectWrite,
        Permissions.Portfolio.CaseStudyPublish, Permissions.Portfolio.ClientPublish,
        Permissions.Lead.FormRead,
        Permissions.Reporting.Dashboard,
    ];

    private static readonly string[] SalesPermissions =
    [
        Permissions.Catalog.ProductRead, Permissions.Catalog.PlanRead, Permissions.Catalog.DemoCredentials,
        Permissions.Catalog.ApiRead, Permissions.Portfolio.ProjectRead,
        Permissions.Content.MediaRead,
        Permissions.Lead.FormRead, Permissions.Lead.SubmissionRead, Permissions.Lead.Read, Permissions.Lead.Write,
        Permissions.Lead.Merge, Permissions.Lead.Spam, Permissions.Lead.ActivityWrite,
        Permissions.Crm.OrganisationRead, Permissions.Crm.OrganisationWrite, Permissions.Crm.ContactWrite,
        Permissions.Sales.QuoteRead, Permissions.Sales.QuoteWrite, Permissions.Sales.QuoteDiscount,
        Permissions.Sales.QuoteSend, Permissions.Sales.QuoteAccept, Permissions.Sales.TenantWrite,
        Permissions.Sales.SubscriptionRead, Permissions.Sales.SubscriptionChange,
        Permissions.Finance.InvoiceRead, Permissions.Finance.InvoiceIssue, Permissions.Finance.PaymentWrite,
        Permissions.Reporting.Read, Permissions.Reporting.Export, Permissions.Reporting.Dashboard,
        Permissions.Integration.OutboxRead,
    ];

    private static readonly string[] AuditorPermissions =
    [
        Permissions.Content.PageRead, Permissions.Content.MediaRead,
        Permissions.Catalog.ProductRead, Permissions.Catalog.PlanRead, Permissions.Catalog.ApiRead,
        Permissions.Portfolio.ProjectRead,
        Permissions.Lead.FormRead, Permissions.Lead.SubmissionRead, Permissions.Lead.Read,
        Permissions.Crm.OrganisationRead,
        Permissions.Sales.QuoteRead, Permissions.Sales.SubscriptionRead,
        Permissions.Finance.InvoiceRead,
        Permissions.Reporting.Read, Permissions.Reporting.Export, Permissions.Reporting.Dashboard,
        Permissions.Administration.UserRead, Permissions.Administration.SettingRead,
        Permissions.Administration.AuditRead,
        Permissions.Integration.OutboxRead,
    ];

    /// <summary>Every permission in the catalogue. The Owner holds all of them by definition.</summary>
    private static readonly string[] OwnerPermissions =
        [.. Permissions.Catalogue.Select(entry => entry.Name)];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Owner] = OwnerPermissions,
            [RoleNames.Sales] = SalesPermissions,
            [RoleNames.Editor] = EditorPermissions,
            [RoleNames.Auditor] = AuditorPermissions,
        };

    public static IReadOnlyList<string> For(string roleName) =>
        ByRole.TryGetValue(roleName, out var permissions) ? permissions : [];

    /// <summary>
    /// Permissions that grant a write. Used by the test that proves the Auditor cannot reach any
    /// of them, which is the property that makes a read-only role actually read-only.
    /// </summary>
    public static bool IsWritePermission(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return permission.Contains(".write", StringComparison.Ordinal)
        || permission.Contains(".publish", StringComparison.Ordinal)
        || permission.Contains(".delete", StringComparison.Ordinal)
        || permission.Contains(".archive", StringComparison.Ordinal)
        || permission.Contains(".assign", StringComparison.Ordinal)
        || permission.Contains(".merge", StringComparison.Ordinal)
        || permission.Contains(".restore", StringComparison.Ordinal)
        || permission.Contains(".issue", StringComparison.Ordinal)
        || permission.Contains(".cancel", StringComparison.Ordinal)
        || permission.Contains(".refund", StringComparison.Ordinal)
        || permission.Contains(".accept", StringComparison.Ordinal)
        || permission.Contains(".send", StringComparison.Ordinal)
        || permission.Contains(".change", StringComparison.Ordinal)
        || permission.Contains(".replay", StringComparison.Ordinal)
        || permission.Contains(".retry", StringComparison.Ordinal)
        || permission.Contains(".run", StringComparison.Ordinal)
        || permission.Contains(".erase", StringComparison.Ordinal)
        || permission.Contains(".discount", StringComparison.Ordinal)
            || permission.Contains(".spam", StringComparison.Ordinal)
            || permission == Permissions.Lead.Submit;
    }
}
