namespace SoftwareManagement.Application.Content;

/// <summary>
/// Reads the settings the owner can change without a deployment: the company's name, its working
/// hours, its currency, the address notifications come from.
///
/// A synchronous read, deliberately. These are a handful of short rows that change perhaps twice a
/// year, so they are loaded once and cached; making every caller await a database round trip for
/// "what are the office hours" would spread asynchrony through code that has no other reason for it.
/// </summary>
public interface ISystemSettings
{
    /// <summary>The value, or null when the key is not set. Callers supply their own fallback.</summary>
    string? Value(string key);

    /// <summary>Forgets the cached values, so the next read sees what was just saved.</summary>
    void Invalidate();
}
