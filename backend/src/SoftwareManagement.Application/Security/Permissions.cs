namespace SoftwareManagement.Application.Security;

/// <summary>
/// The permission catalogue from `docs/blueprint/06-authz.md`. Code never checks a role name; it
/// checks one of these strings, so adding a role later is a data change rather than a code change
/// (A-13, BR-IAM-05).
///
/// Every constant here is seeded into the Permissions table, and the authorization matrix test
/// asserts each one against the real endpoint for every role.
/// </summary>
public static class Permissions
{
    public static class Content
    {
        public const string PageRead = "content.page.read";
        public const string PageWrite = "content.page.write";
        public const string PagePublish = "content.page.publish";
        public const string PageDelete = "content.page.delete";
        public const string VersionRestore = "content.version.restore";
        public const string MediaRead = "content.media.read";
        public const string MediaWrite = "content.media.write";
        public const string MediaDelete = "content.media.delete";
        public const string NavigationWrite = "content.navigation.write";
    }

    public static class Catalog
    {
        public const string ProductRead = "catalog.product.read";
        public const string ProductWrite = "catalog.product.write";
        public const string ProductPublish = "catalog.product.publish";
        public const string ProductArchive = "catalog.product.archive";
        public const string PlanRead = "catalog.plan.read";
        public const string PlanWrite = "catalog.plan.write";
        public const string DemoCredentials = "catalog.demo.credentials";
        public const string ApiRead = "catalog.api.read";
        public const string ApiWrite = "catalog.api.write";
    }

    public static class Portfolio
    {
        public const string ProjectRead = "portfolio.project.read";
        public const string ProjectWrite = "portfolio.project.write";
        public const string CaseStudyPublish = "portfolio.casestudy.publish";
        public const string ClientPublish = "portfolio.client.publish";
    }

    public static class Lead
    {
        public const string FormRead = "lead.form.read";
        public const string FormWrite = "lead.form.write";
        public const string Submit = "lead.submit";
        public const string SubmissionRead = "lead.submission.read";
        public const string Read = "lead.read";
        public const string Write = "lead.write";
        public const string Assign = "lead.assign";
        public const string Merge = "lead.merge";
        public const string Spam = "lead.spam";
        public const string SpamRestore = "lead.spam.restore";
        public const string ActivityWrite = "lead.activity.write";
    }

    public static class Crm
    {
        public const string OrganisationRead = "crm.org.read";
        public const string OrganisationWrite = "crm.org.write";
        public const string OrganisationDelete = "crm.org.delete";
        public const string ContactWrite = "crm.contact.write";
    }

    public static class Sales
    {
        public const string QuoteRead = "sales.quote.read";
        public const string QuoteWrite = "sales.quote.write";
        public const string QuoteDiscount = "sales.quote.discount";
        public const string QuoteSend = "sales.quote.send";
        public const string QuoteAccept = "sales.quote.accept";
        public const string TenantWrite = "sales.tenant.write";
        public const string SubscriptionRead = "sales.subscription.read";
        public const string SubscriptionChange = "sales.subscription.change";
        public const string SubscriptionCancel = "sales.subscription.cancel";
    }

    public static class Finance
    {
        public const string InvoiceRead = "finance.invoice.read";
        public const string InvoiceIssue = "finance.invoice.issue";
        public const string InvoiceCancel = "finance.invoice.cancel";
        public const string PaymentWrite = "finance.payment.write";
        public const string PaymentRefund = "finance.payment.refund";
    }

    public static class Reporting
    {
        public const string Read = "report.read";
        public const string Export = "report.export";
        public const string Dashboard = "dashboard.read";
    }

    public static class Administration
    {
        public const string UserRead = "admin.user.read";
        public const string UserWrite = "admin.user.write";
        public const string RoleAssign = "admin.role.assign";
        public const string SettingRead = "admin.setting.read";
        public const string SettingWrite = "admin.setting.write";
        public const string AuditRead = "admin.audit.read";
        public const string ImportRun = "admin.import.run";
        public const string PrivacyExport = "admin.privacy.export";
        public const string PrivacyErase = "admin.privacy.erase";
    }

    public static class Integration
    {
        public const string WebhookWrite = "integration.webhook.write";
        public const string WebhookReplay = "integration.webhook.replay";
        public const string TemplateWrite = "notify.template.write";
        public const string OutboxRead = "notify.outbox.read";
        public const string OutboxRetry = "notify.outbox.retry";
    }

    /// <summary>Every permission that exists, with the category it is seeded under.</summary>
    public static IReadOnlyList<(string Name, string Category, string Description)> Catalogue { get; } =
    [
        (Content.PageRead, "Content", "Read pages, including drafts"),
        (Content.PageWrite, "Content", "Create and edit pages and their sections"),
        (Content.PagePublish, "Content", "Publish, unpublish and schedule content"),
        (Content.PageDelete, "Content", "Delete a page that nothing references"),
        (Content.VersionRestore, "Content", "Restore an earlier version of a content item"),
        (Content.MediaRead, "Content", "Browse the media library"),
        (Content.MediaWrite, "Content", "Upload media"),
        (Content.MediaDelete, "Content", "Delete unused media"),
        (Content.NavigationWrite, "Content", "Maintain the header and footer menus"),

        (Catalog.ProductRead, "Catalogue", "Read products, including drafts"),
        (Catalog.ProductWrite, "Catalogue", "Create and edit products, features and screenshots"),
        (Catalog.ProductPublish, "Catalogue", "Publish a product to the public catalogue"),
        (Catalog.ProductArchive, "Catalogue", "Archive a retired product"),
        (Catalog.PlanRead, "Catalogue", "Read pricing plans"),
        (Catalog.PlanWrite, "Catalogue", "Create and edit pricing plans and plan features"),
        (Catalog.DemoCredentials, "Catalogue", "See shared demo credentials"),
        (Catalog.ApiRead, "Catalogue", "Read the public API catalogue"),
        (Catalog.ApiWrite, "Catalogue", "Maintain API entries and versions"),

        (Portfolio.ProjectRead, "Portfolio", "Read projects and case studies"),
        (Portfolio.ProjectWrite, "Portfolio", "Create and edit projects and case studies"),
        (Portfolio.CaseStudyPublish, "Portfolio", "Publish a case study"),
        (Portfolio.ClientPublish, "Portfolio", "Publish a client name or logo"),

        (Lead.FormRead, "Leads", "Read public form definitions"),
        (Lead.FormWrite, "Leads", "Edit form definitions and consent text"),
        (Lead.Submit, "Leads", "Submit a public form"),
        (Lead.SubmissionRead, "Leads", "Read raw form submissions"),
        (Lead.Read, "Leads", "Read leads and their activities"),
        (Lead.Write, "Leads", "Edit a lead and move it through the pipeline"),
        (Lead.Assign, "Leads", "Assign a lead to a user"),
        (Lead.Merge, "Leads", "Merge duplicate leads"),
        (Lead.Spam, "Leads", "Mark a lead as spam"),
        (Lead.SpamRestore, "Leads", "Restore a lead wrongly marked as spam"),
        (Lead.ActivityWrite, "Leads", "Record notes, calls and follow-ups"),

        (Crm.OrganisationRead, "Customers", "Read organisations and contacts"),
        (Crm.OrganisationWrite, "Customers", "Create and edit organisations"),
        (Crm.OrganisationDelete, "Customers", "Deactivate an organisation"),
        (Crm.ContactWrite, "Customers", "Create and edit contacts"),

        (Sales.QuoteRead, "Sales", "Read quotes"),
        (Sales.QuoteWrite, "Sales", "Create and edit draft quotes"),
        (Sales.QuoteDiscount, "Sales", "Apply a discount within the approval limit"),
        (Sales.QuoteSend, "Sales", "Send a quote to a customer"),
        (Sales.QuoteAccept, "Sales", "Record a quote as accepted"),
        (Sales.TenantWrite, "Sales", "Record a sold tenant"),
        (Sales.SubscriptionRead, "Sales", "Read subscriptions"),
        (Sales.SubscriptionChange, "Sales", "Change a plan or seat count"),
        (Sales.SubscriptionCancel, "Sales", "Cancel a subscription"),

        (Finance.InvoiceRead, "Finance", "Read invoices and payments"),
        (Finance.InvoiceIssue, "Finance", "Issue an invoice"),
        (Finance.InvoiceCancel, "Finance", "Cancel an invoice with a reason"),
        (Finance.PaymentWrite, "Finance", "Record a payment received"),
        (Finance.PaymentRefund, "Finance", "Record a refund"),

        (Reporting.Read, "Reporting", "Open reports"),
        (Reporting.Export, "Reporting", "Export a report"),
        (Reporting.Dashboard, "Reporting", "Open the dashboard"),

        (Administration.UserRead, "Administration", "Read back-office users"),
        (Administration.UserWrite, "Administration", "Create and deactivate users"),
        (Administration.RoleAssign, "Administration", "Assign roles"),
        (Administration.SettingRead, "Administration", "Read settings, with secrets masked"),
        (Administration.SettingWrite, "Administration", "Change settings"),
        (Administration.AuditRead, "Administration", "Read the audit trail"),
        (Administration.ImportRun, "Administration", "Run an import"),
        (Administration.PrivacyExport, "Administration", "Export one person's data"),
        (Administration.PrivacyErase, "Administration", "Erase one person's data"),

        (Integration.WebhookWrite, "Integrations", "Manage webhook endpoints"),
        (Integration.WebhookReplay, "Integrations", "Replay a failed delivery"),
        (Integration.TemplateWrite, "Integrations", "Edit email templates"),
        (Integration.OutboxRead, "Integrations", "Read the outbox and delivery log"),
        (Integration.OutboxRetry, "Integrations", "Retry a dead-lettered message"),
    ];
}
