using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Domain.Settings;

/// <summary>
/// A typed key-value setting the owner can change without a deployment: company profile, business
/// hours, timezone, provider keys. A setting marked secret never returns its value through the API;
/// the row holds a reference to a configuration key, not the secret itself (REQ-ADM-003).
/// </summary>
public class SystemSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public SettingValueType ValueType { get; set; } = SettingValueType.Text;

    public string Category { get; set; } = string.Empty;

    public bool IsSecret { get; set; }
}

public enum SettingValueType
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    Json = 3,
    Secret = 4,
}
