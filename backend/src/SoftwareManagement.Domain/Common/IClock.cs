namespace SoftwareManagement.Domain.Common;

/// <summary>
/// Time is a dependency, not an ambient global. Every SLA window, retry schedule and
/// renewal date is computed from this so the rules can be tested at their boundaries
/// (BR-LEAD-10, BR-NOTIF-02, BR-SALE-11).
/// </summary>
public interface IClock
{
    /// <summary>The current instant in UTC. Never local time (A-08).</summary>
    DateTime UtcNow { get; }
}
