using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Infrastructure.Time;

/// <summary>The real clock. Tests substitute a fixed clock instead of waiting.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
