using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Domain.Notifications;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// The forms the public site offers and the messages it sends.
///
/// They are seeded rather than hard-coded so the owner can reword a question, change the consent
/// text or rewrite an acknowledgement without a deployment (REQ-ADM-012, REQ-NOTIF-006). Seeding
/// only ever inserts what is missing, so an edited form is never overwritten by a restart.
/// </summary>
public sealed partial class DatabaseSeeder
{
    private const string DefaultConsentText =
        "I agree that Software Management may store these details and contact me about this enquiry. " +
        "They will not be sold or shared, and I can ask for them to be deleted at any time.";

    private async Task SeedFormsAsync(CancellationToken cancellationToken)
    {
        var notify = _configuration["Seed:OwnerEmail"] ?? "owner@softwaremanagement.test";

        var existing = await _dbContext.FormDefinitions
            .Select(f => f.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var forms = new List<FormDefinition>();

        if (!existing.Contains(FormDefinition.Contact, StringComparer.Ordinal))
        {
            var contact = NewForm(
                FormDefinition.Contact,
                "Talk to us",
                "Tell us what you are trying to run better. We answer every enquiry ourselves.",
                "Send enquiry",
                "Thank you. We have your enquiry and will reply within one working day.",
                notify);

            AddField(contact, "fullName", "Your name", FormFieldType.Text, required: true, maxLength: 150, order: 1);
            AddField(contact, "email", "Email", FormFieldType.Email, required: false, maxLength: 256, order: 2);
            AddField(contact, "phone", "Phone", FormFieldType.Phone, required: false, maxLength: 20, order: 3);
            AddField(contact, "companyName", "Organisation", FormFieldType.Text, required: false, maxLength: 200, order: 4);
            AddField(contact, "message", "What do you need?", FormFieldType.TextArea, required: false, maxLength: 4000, order: 5);
            forms.Add(contact);
        }

        if (!existing.Contains(FormDefinition.RequestDemo, StringComparer.Ordinal))
        {
            var demo = NewForm(
                FormDefinition.RequestDemo,
                "See it working",
                "We will walk you through the product on a call, using your own examples.",
                "Request a demo",
                "Thank you. We will be in touch to arrange a time that suits you.",
                notify);

            AddField(demo, "fullName", "Your name", FormFieldType.Text, required: true, maxLength: 150, order: 1);
            AddField(demo, "email", "Email", FormFieldType.Email, required: true, maxLength: 256, order: 2);
            AddField(demo, "phone", "Phone", FormFieldType.Phone, required: false, maxLength: 20, order: 3);
            AddField(demo, "companyName", "Organisation", FormFieldType.Text, required: false, maxLength: 200, order: 4);
            AddField(demo, "product", "Which product?", FormFieldType.ProductPicker, required: false, maxLength: 120, order: 5);
            AddField(demo, "preferredTime", "When suits you?", FormFieldType.Text, required: false, maxLength: 120, order: 6);
            AddField(demo, "message", "Anything we should know?", FormFieldType.TextArea, required: false, maxLength: 4000, order: 7);
            forms.Add(demo);
        }

        if (!existing.Contains(FormDefinition.RequestQuote, StringComparer.Ordinal))
        {
            var quote = NewForm(
                FormDefinition.RequestQuote,
                "Get a price",
                "Tell us the size of the operation and we will send a written quote.",
                "Request a quote",
                "Thank you. We will send a written quote, usually within two working days.",
                notify);

            AddField(quote, "fullName", "Your name", FormFieldType.Text, required: true, maxLength: 150, order: 1);
            AddField(quote, "email", "Email", FormFieldType.Email, required: true, maxLength: 256, order: 2);
            AddField(quote, "phone", "Phone", FormFieldType.Phone, required: false, maxLength: 20, order: 3);
            AddField(quote, "companyName", "Organisation", FormFieldType.Text, required: true, maxLength: 200, order: 4);

            // A quote without a product is not a quote, so this picker is the one required choice on
            // the form (REQ-LEAD-003).
            AddField(quote, "product", "Which product?", FormFieldType.ProductPicker, required: true, maxLength: 120, order: 5);
            AddField(quote, "seats", "How many people will use it?", FormFieldType.Text, required: false, maxLength: 20, order: 6);
            AddField(quote, "message", "Anything else?", FormFieldType.TextArea, required: false, maxLength: 4000, order: 7);
            forms.Add(quote);
        }

        if (forms.Count > 0)
        {
            _dbContext.FormDefinitions.AddRange(forms);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogSeededForms(_logger, forms.Count);
        }
    }

    private async Task SeedEmailTemplatesAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.EmailTemplates
            .Select(t => t.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var templates = new List<EmailTemplate>();

        if (!existing.Contains(EmailTemplate.LeadAcknowledgement, StringComparer.Ordinal))
        {
            templates.Add(new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Key = EmailTemplate.LeadAcknowledgement,
                Subject = "We have your enquiry ({{reference}})",
                TextBody =
                    "Dear {{fullName}},\n\n" +
                    "Thank you for getting in touch. Your enquiry reference is {{reference}}.\n\n" +
                    "A person reads every enquiry here, and we will reply within one working day.\n\n" +
                    "{{companyName}}",
                HtmlBody =
                    "<p>Dear {{fullName}},</p>" +
                    "<p>Thank you for getting in touch. Your enquiry reference is <strong>{{reference}}</strong>.</p>" +
                    "<p>A person reads every enquiry here, and we will reply within one working day.</p>" +
                    "<p>{{companyName}}</p>",
                PlaceholdersJson = """["fullName","reference","companyName"]""",
                CreatedBy = "seed",
            });
        }

        if (!existing.Contains(EmailTemplate.LeadOwnerAlert, StringComparer.Ordinal))
        {
            templates.Add(new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Key = EmailTemplate.LeadOwnerAlert,
                Subject = "New enquiry: {{fullName}} ({{reference}})",
                TextBody =
                    "{{fullName}} sent the {{formTitle}} form.\n\n" +
                    "Contact: {{contact}}\n" +
                    "Reply due by: {{dueAt}}\n\n" +
                    "{{message}}\n\n" +
                    "Reference {{reference}}.",
                HtmlBody =
                    "<p><strong>{{fullName}}</strong> sent the {{formTitle}} form.</p>" +
                    "<p>Contact: {{contact}}<br>Reply due by: {{dueAt}}</p>" +
                    "<blockquote>{{message}}</blockquote>" +
                    "<p>Reference {{reference}}.</p>",
                PlaceholdersJson = """["fullName","reference","formTitle","message","contact","dueAt"]""",
                CreatedBy = "seed",
            });
        }

        if (templates.Count > 0)
        {
            _dbContext.EmailTemplates.AddRange(templates);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogSeededTemplates(_logger, templates.Count);
        }
    }

    private static FormDefinition NewForm(
        string key,
        string title,
        string intro,
        string submitLabel,
        string successMessage,
        string notifyEmail) =>
        new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            Title = title,
            Intro = intro,
            SubmitLabel = submitLabel,
            SuccessMessage = successMessage,
            ConsentText = DefaultConsentText,
            ConsentVersion = 1,
            NotifyEmails = notifyEmail,
            AcknowledgementTemplateKey = EmailTemplate.LeadAcknowledgement,
            IsEnabled = true,
            CreatedBy = "seed",
        };

    private static void AddField(
        FormDefinition form,
        string name,
        string label,
        FormFieldType type,
        bool required,
        int maxLength,
        int order) =>
        form.Fields.Add(new FormField
        {
            Id = Guid.NewGuid(),
            FormDefinitionId = form.Id,
            Name = name,
            Label = label,
            FieldType = type,
            IsRequired = required,
            MaxLength = maxLength,
            SortOrder = order,
            CreatedBy = "seed",
        });
}
