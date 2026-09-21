namespace SoftwareManagement.Domain.Leads;

/// <summary>
/// Which stage a lead may move to, and what the move has to carry with it.
///
/// The rules live here rather than in the controller because they are the shape of the funnel, and
/// a funnel that means different things in two places means nothing anywhere. BR-LEAD-09 in one
/// sentence: forward through New, Contacted, Qualified, Converted; skipping Contacted is allowed
/// only with a reason recorded; any move to Disqualified needs a reason of at least ten characters.
/// </summary>
public static class LeadStageMachine
{
    /// <summary>
    /// Ten characters, because "no" and "n/a" are what a required free-text box collects otherwise,
    /// and a funnel full of them cannot be read six months later (BR-LEAD-09).
    /// </summary>
    public const int MinimumReasonLength = 10;

    /// <summary>The ordinary forward path. Position in this list is what "skipping" is measured against.</summary>
    private static readonly LeadStage[] Pipeline =
        [LeadStage.New, LeadStage.Contacted, LeadStage.Qualified, LeadStage.Converted];

    /// <summary>
    /// Stages nobody reaches by moving a lead: they are the result of a different operation, with
    /// its own permission and its own audit trail.
    /// </summary>
    private static readonly LeadStage[] NotReachableByStageChange =
        [LeadStage.Spam, LeadStage.Merged, LeadStage.Converted];

    public static StageMoveVerdict Check(LeadStage from, LeadStage to, string? reason)
    {
        if (from == to)
        {
            return StageMoveVerdict.Refused("stage", "The lead is already at that stage.");
        }

        if (Array.IndexOf(NotReachableByStageChange, to) >= 0)
        {
            // Converted is reached by converting, which creates the customer record; spam and
            // merged by their own endpoints. Allowing the stage field to set them would leave a
            // lead marked Converted with nothing to show for it.
            return StageMoveVerdict.Refused("stage", $"A lead does not move to {to} through a stage change.");
        }

        if (from is LeadStage.Spam or LeadStage.Merged)
        {
            return StageMoveVerdict.Refused("stage", $"A lead marked {from} is not worked through the pipeline.");
        }

        if (to == LeadStage.Disqualified)
        {
            return ReasonOf(reason) is { } given && given.Length >= MinimumReasonLength
                ? StageMoveVerdict.Allowed(given)
                : StageMoveVerdict.Refused(
                    "reason",
                    $"Say why in at least {MinimumReasonLength} characters. Whoever reads the funnel later is not in the room now.");
        }

        // Nurturing and Disqualified are side stages: a lead can come back from either, and where
        // it left the pipeline is not a reason to refuse the return.
        var fromIndex = Array.IndexOf(Pipeline, from);
        var toIndex = Array.IndexOf(Pipeline, to);

        if (fromIndex < 0 || toIndex < 0)
        {
            return StageMoveVerdict.Allowed(ReasonOf(reason));
        }

        if (toIndex < fromIndex)
        {
            // Backwards happens - a "qualified" lead turns out not to be - and it is recorded
            // rather than refused, because the alternative is people leaving the stage wrong.
            return StageMoveVerdict.Allowed(ReasonOf(reason));
        }

        if (toIndex - fromIndex > 1)
        {
            return ReasonOf(reason) is { } jump && jump.Length >= MinimumReasonLength
                ? StageMoveVerdict.Allowed(jump)
                : StageMoveVerdict.Refused(
                    "reason",
                    $"Skipping {Pipeline[fromIndex + 1]} needs a reason of at least {MinimumReasonLength} characters.");
        }

        return StageMoveVerdict.Allowed(ReasonOf(reason));
    }

    private static string? ReasonOf(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}

/// <summary>
/// The answer, with the reason trimmed as it will be stored, so the caller cannot trim it
/// differently and store something the check never saw.
/// </summary>
public sealed record StageMoveVerdict(bool IsAllowed, string? Field, string? Message, string? Reason)
{
    public static StageMoveVerdict Allowed(string? reason) => new(true, null, null, reason);

    public static StageMoveVerdict Refused(string field, string message) => new(false, field, message, null);
}
