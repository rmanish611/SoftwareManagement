using System.Text.Json;
using System.Text.RegularExpressions;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Domain.Notifications;

namespace SoftwareManagement.Infrastructure.Notifications;

/// <summary>
/// Fills a template's placeholders, or refuses.
///
/// Two rules, and both are about what must never reach a customer. A message still holding an
/// unfilled placeholder is not sent: "Dear {{fullName}}," is worse than a message that did not go
/// out and was logged (REQ-NOTIF-003). And a composed body is scanned for anything that looks like a
/// credential, because an email is forwarded, quoted and archived by people who were never meant to
/// have it (BR-NOTIF-05).
/// </summary>
public sealed partial class EmailComposer : IEmailComposer
{
    public ComposedEmail Compose(EmailTemplate emailTemplate, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(emailTemplate);
        ArgumentNullException.ThrowIfNull(values);

        var declared = ReadDeclaredPlaceholders(emailTemplate.PlaceholdersJson);

        // A value nobody declared is a template and a caller that disagree about the contract. It is
        // reported rather than quietly dropped, because the usual cause is a renamed placeholder
        // that now silently renders as nothing.
        var undeclared = values.Keys.Where(k => declared.Count > 0 && !declared.Contains(k)).ToList();
        if (undeclared.Count > 0)
        {
            return ComposedEmail.Failed(
                $"The template \"{emailTemplate.Key}\" does not declare: {string.Join(", ", undeclared)}.");
        }

        var subject = Fill(emailTemplate.Subject, values);
        var html = Fill(emailTemplate.HtmlBody, values);
        var text = Fill(emailTemplate.TextBody, values);

        var unfilled = PlaceholderPattern()
            .Matches(subject + "\n" + html + "\n" + text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unfilled.Count > 0)
        {
            return ComposedEmail.Failed(
                $"No value was supplied for: {string.Join(", ", unfilled)}. The message was not queued.");
        }

        var leaked = SecretPattern().Match(html + "\n" + text);
        if (leaked.Success)
        {
            return ComposedEmail.Failed(
                "The composed message contains something shaped like a credential. It was not queued.");
        }

        return ComposedEmail.Ok(subject, html, text);
    }

    private static HashSet<string> ReadDeclaredPlaceholders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?.ToHashSet(StringComparer.Ordinal) ?? [];
        }
        catch (JsonException)
        {
            // A malformed declaration is treated as "declares nothing", which disables only the
            // undeclared-value check. The unfilled-placeholder check below still protects the send.
            return [];
        }
    }

    private static string Fill(string body, IReadOnlyDictionary<string, string> values)
    {
        var filled = body;

        foreach (var (key, value) in values)
        {
            filled = filled.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }

        return filled;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// Deliberately blunt. A false positive costs one unsent message and a log line; a false
    /// negative sends a customer somebody's password.
    /// </summary>
    [GeneratedRegex(
        @"(?i)\b(password|passwd|secret|api[_-]?key|access[_-]?token|bearer)\b\s*[:=]\s*\S{6,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}
