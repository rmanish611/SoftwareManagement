namespace SoftwareManagement.Application.Common;

/// <summary>
/// Masks personal details before they reach a log.
///
/// Logs are read by people who have no business knowing who enquired, they are copied into support
/// tickets, and they outlive the retention period of the record itself. An address in a log line is
/// a copy of personal data nobody decided to keep, so it never goes in whole (NFR-PRIV-03).
///
/// The masked form still identifies the row when read beside the database, which is what a log is
/// for; it just does not hand the address to whoever is reading.
/// </summary>
public static class PrivacyMask
{
    /// <summary>
    /// <c>anita@example.test</c> becomes <c>a***@example.test</c>. The domain is kept because it is
    /// what makes a delivery problem diagnosable, and it names an organisation rather than a person.
    /// </summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "(none)";
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0)
        {
            // Not an address at all. Whatever it is, it is not going in a log intact.
            return "***";
        }

        return string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    /// <summary>A phone number keeps its last two digits, which is enough to match against a record.</summary>
    public static string Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "(none)";
        }

        var digits = phone.Where(char.IsDigit).ToArray();

        return digits.Length <= 2
            ? "***"
            : "***" + new string(digits[^2..]);
    }

    /// <summary>
    /// An IP address keeps its network and loses its host: <c>203.0.113.42</c> becomes
    /// <c>203.0.113.x</c>. Enough to see a flood coming from one place, not enough to point at one
    /// household.
    /// </summary>
    public static string IpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return "(none)";
        }

        var lastDot = ipAddress.LastIndexOf('.');
        if (lastDot > 0)
        {
            return string.Concat(ipAddress.AsSpan(0, lastDot + 1), "x");
        }

        // IPv6: keep the first two groups, which name the network.
        var groups = ipAddress.Split(':');
        return groups.Length > 2 ? string.Join(':', groups.Take(2)) + "::x" : "***";
    }
}
