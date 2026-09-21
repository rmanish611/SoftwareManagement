namespace SoftwareManagement.Application.Portfolio;

/// <summary>
/// Publishing a project or an API entry, and the rules that decide whether it may go out.
///
/// The refusals here are all about what a reader would be told rather than about what a form
/// contained: a case study with no measured outcome, a client named without permission, a version
/// deprecated with a fortnight's notice. Each one is a promise to somebody outside the company.
/// </summary>
public interface IPortfolioService
{
    Task<PortfolioResult> PublishProjectAsync(Guid projectId, string actor, CancellationToken cancellationToken);

    Task<PortfolioResult> PublishApiEntryAsync(Guid entryId, string actor, CancellationToken cancellationToken);

    /// <summary>Makes one version the current one, clearing whichever held it before (BR-API-02).</summary>
    Task<PortfolioResult> MakeCurrentAsync(Guid versionId, string actor, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a version deprecated with a sunset date. Refused if the date gives less than ninety
    /// days, or if the version is the current one and nothing has replaced it (BR-API-03).
    /// </summary>
    Task<PortfolioResult> DeprecateAsync(Guid versionId, DateOnly sunsetDate, string actor, CancellationToken cancellationToken);
}

public enum PortfolioOutcome
{
    Done = 0,
    NotFound = 1,
    Refused = 2,
    Conflict = 3,
}

public sealed record PortfolioResult(
    PortfolioOutcome Outcome,
    string? Code = null,
    string? Field = null,
    string? Message = null,
    IReadOnlyList<string>? Shortfalls = null)
{
    public static PortfolioResult Done() => new(PortfolioOutcome.Done);

    public static PortfolioResult NotFound() => new(PortfolioOutcome.NotFound);

    public static PortfolioResult Refused(string code, string? field, string message, IReadOnlyList<string>? shortfalls = null) =>
        new(PortfolioOutcome.Refused, code, field, message, shortfalls);

    public static PortfolioResult Conflict(string code, string message) =>
        new(PortfolioOutcome.Conflict, code, null, message);
}
