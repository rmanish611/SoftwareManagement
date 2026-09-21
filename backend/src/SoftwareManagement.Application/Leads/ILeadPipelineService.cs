using SoftwareManagement.Domain.Leads;

namespace SoftwareManagement.Application.Leads;

/// <summary>
/// Working a lead: moving it, recording what happened, and the three operations that change what a
/// lead is rather than where it is - merging, converting and marking it spam.
///
/// Each one returns an outcome rather than throwing, because every one of them has a refusal the
/// caller has to turn into a specific status code, and exceptions as flow control lose which.
/// </summary>
public interface ILeadPipelineService
{
    Task<PipelineResult> ChangeStageAsync(
        Guid leadId, LeadStage toStage, string? reason, string actor, CancellationToken cancellationToken);

    Task<PipelineResult> AddActivityAsync(
        Guid leadId, NewActivity activity, string actor, CancellationToken cancellationToken);

    /// <summary>
    /// Merges <paramref name="otherLeadId"/> into the same conversation as <paramref name="leadId"/>.
    ///
    /// Which of the two survives is decided by the rule, not by the order the caller names them:
    /// the earlier record wins, so its creation time - and the SLA measured from it - is preserved
    /// (BR-LEAD-08).
    /// </summary>
    Task<PipelineResult> MergeAsync(Guid leadId, Guid otherLeadId, string actor, CancellationToken cancellationToken);

    Task<PipelineResult> ConvertAsync(Guid leadId, ConversionRequest request, string actor, CancellationToken cancellationToken);

    Task<PipelineResult> SetSpamAsync(Guid leadId, bool isSpam, string actor, CancellationToken cancellationToken);
}

/// <summary>
/// The scheduled half of the pipeline: follow-ups that have come due and conversations that have
/// gone quiet. Exposed as an interface so a test can run one pass deterministically instead of
/// waiting for a timer.
/// </summary>
public interface ILeadSweep
{
    Task<SweepResult> RunOnceAsync(CancellationToken cancellationToken);
}

public sealed record SweepResult(int RemindersQueued, int ColdLeadsQueued);

public sealed record NewActivity(
    LeadActivityType ActivityType,
    LeadActivityDirection Direction,
    string? Body,
    DateTime? OccurredAtUtc,
    DateTime? DueAtUtc);

/// <summary>
/// What converting a lead needs. Either an existing organisation or enough to create one: a
/// conversion with neither is refused rather than guessed at, because a customer record with a
/// blank name is worse than no customer record (REQ-LEAD-015).
/// </summary>
public sealed record ConversionRequest(
    Guid? OrganisationId,
    string? OrganisationName,
    string? ContactFullName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactJobTitle);

public enum PipelineOutcome
{
    Done = 0,
    NotFound = 1,
    Refused = 2,
    Conflict = 3,
}

public sealed record PipelineResult(
    PipelineOutcome Outcome,
    string? Code = null,
    string? Field = null,
    string? Message = null,
    Guid? SubjectId = null)
{
    public static PipelineResult Done(Guid? subjectId = null) => new(PipelineOutcome.Done, SubjectId: subjectId);

    public static PipelineResult NotFound() => new(PipelineOutcome.NotFound);

    public static PipelineResult Refused(string code, string? field, string message) =>
        new(PipelineOutcome.Refused, code, field, message);

    public static PipelineResult Conflict(string code, string message) =>
        new(PipelineOutcome.Conflict, code, null, message);
}
