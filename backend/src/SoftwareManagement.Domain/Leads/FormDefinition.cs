using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Leads;

/// <summary>
/// One public form: what it asks, what it says, and what a visitor is agreeing to by sending it.
///
/// The forms are data rather than code so the owner can change the wording, add a question or turn
/// a form off without a deployment (REQ-ADM-012). The consent text is versioned, because a record
/// of consent is worthless unless it says which words the person actually agreed to.
/// </summary>
public class FormDefinition : AuditableEntity
{
    public const string Contact = "contact";
    public const string RequestDemo = "request-demo";
    public const string RequestQuote = "request-quote";

    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Intro { get; set; }

    public string SubmitLabel { get; set; } = "Send";

    public string SuccessMessage { get; set; } = string.Empty;

    public string ConsentText { get; set; } = string.Empty;

    public int ConsentVersion { get; set; } = 1;

    /// <summary>Comma-separated addresses alerted when this form is used.</summary>
    public string NotifyEmails { get; set; } = string.Empty;

    public string? AcknowledgementTemplateKey { get; set; }

    public bool IsEnabled { get; set; } = true;

    public ICollection<FormField> Fields { get; } = [];
}

public class FormField : AuditableEntity
{
    public Guid FormDefinitionId { get; set; }

    public FormDefinition? Form { get; set; }

    /// <summary>The key in the submitted payload, for example <c>fullName</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public FormFieldType FieldType { get; set; } = FormFieldType.Text;

    public bool IsRequired { get; set; }

    public int? MaxLength { get; set; }

    /// <summary>The choices for a Select field, as a JSON array of strings.</summary>
    public string? OptionsJson { get; set; }

    public int SortOrder { get; set; }
}

public enum FormFieldType
{
    Text = 0,
    Email = 1,
    Phone = 2,
    TextArea = 3,
    Select = 4,
    Checkbox = 5,

    /// <summary>Rendered as a list of published products, so the picker cannot go stale.</summary>
    ProductPicker = 6,
}

/// <summary>
/// One thing a visitor sent, exactly as they sent it. Append-only: this is the record that settles
/// a later argument about what was submitted, so it is never edited (A-16).
/// </summary>
public class FormSubmission : AuditableEntity
{
    public Guid FormDefinitionId { get; set; }

    public FormDefinition? Form { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// A hash of the meaningful content. Two submissions of the same thing from the same address
    /// within ten minutes are one person double-clicking, not two enquiries (BR-LEAD-05).
    /// </summary>
    public string MessageHash { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    public string? Referrer { get; set; }

    public CaptchaOutcome CaptchaOutcome { get; set; } = CaptchaOutcome.Unverified;

    /// <summary>
    /// A submission that tripped the honeypot. It is stored, answered normally so the bot learns
    /// nothing, and never notified or counted (BR-LEAD-03).
    /// </summary>
    public bool IsSpam { get; set; }

    public Guid? LeadId { get; set; }

    public Lead? Lead { get; set; }

    public ConsentRecord? Consent { get; set; }
}

public enum CaptchaOutcome
{
    Unverified = 0,
    Passed = 1,
    Failed = 2,
}

/// <summary>
/// Proof that a person agreed to something, and to which words.
///
/// Under the DPDP Act consent must be specific, informed and demonstrable. A boolean would not be
/// demonstrable, so the exact text, its version, the purpose, the time and the address are all kept,
/// and the row is never edited (BR-LEAD-06, A-16).
/// </summary>
public class ConsentRecord : AuditableEntity
{
    public Guid FormSubmissionId { get; set; }

    public FormSubmission? Submission { get; set; }

    public string ConsentText { get; set; } = string.Empty;

    public int ConsentVersion { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public DateTime GivenAtUtc { get; set; }

    public string IpAddress { get; set; } = string.Empty;
}
