using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Domain.Leads;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// Working a lead through the pipeline.
///
/// Everything here writes an activity as well as changing the lead, in the same transaction. That
/// is the whole point of the timeline: six months later the only reliable account of what happened
/// to an enquiry is the one the system wrote down at the time, and a stage that changed with no
/// record of who moved it or why is the gap that account cannot fill (BR-LEAD-09).
/// </summary>
public sealed partial class LeadPipelineService(
    AppDbContext dbContext,
    ISlaCalculator sla,
    IClock clock,
    ILogger<LeadPipelineService> logger) : ILeadPipelineService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ISlaCalculator _sla = sla;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadPipelineService> _logger = logger;

    public async Task<PipelineResult> ChangeStageAsync(
        Guid leadId, LeadStage toStage, string? reason, string actor, CancellationToken cancellationToken)
    {
        var lead = await _dbContext.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return PipelineResult.NotFound();
        }

        var verdict = LeadStageMachine.Check(lead.Stage, toStage, reason);

        if (!verdict.IsAllowed)
        {
            return PipelineResult.Refused("STAGE_NOT_ALLOWED", verdict.Field, verdict.Message!);
        }

        var from = lead.Stage;
        lead.Stage = toStage;
        lead.ModifiedBy = actor;

        if (toStage == LeadStage.Disqualified)
        {
            lead.DisqualifyReason = verdict.Reason;
        }

        _dbContext.LeadActivities.Add(StageChange(lead.Id, from, toStage, verdict.Reason, actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineResult.Done(lead.Id);
    }

    public async Task<PipelineResult> AddActivityAsync(
        Guid leadId, NewActivity activity, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var lead = await _dbContext.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return PipelineResult.NotFound();
        }

        if (activity.ActivityType is LeadActivityType.StageChange or LeadActivityType.SystemEvent)
        {
            // These are written by the system as the record of something it did. Letting a client
            // post one would mean the timeline could be made to say a stage changed when it did not.
            return PipelineResult.Refused(
                "ACTIVITY_NOT_ALLOWED", "activityType", $"{activity.ActivityType} entries are written by the system.");
        }

        if (string.IsNullOrWhiteSpace(activity.Body))
        {
            return PipelineResult.Refused("ACTIVITY_EMPTY", "body", "Say what happened. An empty note is not a record.");
        }

        var now = _clock.UtcNow;
        var occurred = activity.OccurredAtUtc ?? now;

        // Logging Friday's call on Monday is ordinary and allowed; logging next Friday's call is
        // not, because the SLA clock would then stop before the reply existed.
        if (occurred > now.AddMinutes(5))
        {
            return PipelineResult.Refused("ACTIVITY_IN_FUTURE", "occurredAtUtc", "An activity cannot have happened yet.");
        }

        var row = new LeadActivity
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            ActivityType = activity.ActivityType,
            Direction = activity.Direction,
            Body = activity.Body.Trim(),
            OccurredAtUtc = occurred,
            DueAtUtc = activity.DueAtUtc,
            IsCompleted = false,
            CreatedBy = actor,
        };

        _dbContext.LeadActivities.Add(row);

        // The first thing that actually went to the enquirer stops the clock. Recording it on the
        // lead rather than recomputing it from the timeline keeps the SLA report one query
        // (BR-LEAD-10, NFR-PERF-05).
        if (lead.FirstResponseAtUtc is null && row.IsFirstResponseCandidate())
        {
            lead.FirstResponseAtUtc = occurred;
            lead.ModifiedBy = actor;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineResult.Done(row.Id);
    }

    public async Task<PipelineResult> MergeAsync(Guid leadId, Guid otherLeadId, string actor, CancellationToken cancellationToken)
    {
        if (leadId == otherLeadId)
        {
            return PipelineResult.Refused("MERGE_SELF", "otherLeadId", "A lead cannot be merged into itself.");
        }

        var leads = await _dbContext.Leads
            .Where(l => l.Id == leadId || l.Id == otherLeadId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (leads.Count != 2)
        {
            return PipelineResult.NotFound();
        }

        if (leads.Any(l => l.Stage == LeadStage.Merged))
        {
            return PipelineResult.Conflict("ALREADY_MERGED", "One of these has already been merged into another lead.");
        }

        // The survivor is chosen by the rule and not by which one the caller named first: the
        // earlier record wins, so the conversation keeps the creation time its SLA was measured
        // from, and merging the pair the other way round gives the same answer (BR-LEAD-08).
        var survivor = leads.OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id).First();
        var absorbed = leads.First(l => l.Id != survivor.Id);

        await _dbContext.LeadActivities
            .Where(a => a.LeadId == absorbed.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LeadId, survivor.Id), cancellationToken)
            .ConfigureAwait(false);

        absorbed.Stage = LeadStage.Merged;
        absorbed.MergedIntoLeadId = survivor.Id;
        absorbed.ModifiedBy = actor;

        // A reply to either conversation answered the person, so the survivor keeps the earlier of
        // the two response times rather than losing one.
        if (absorbed.FirstResponseAtUtc is { } absorbedReply
            && (survivor.FirstResponseAtUtc is null || absorbedReply < survivor.FirstResponseAtUtc))
        {
            survivor.FirstResponseAtUtc = absorbedReply;
        }

        survivor.ModifiedBy = actor;

        _dbContext.LeadActivities.Add(SystemEvent(
            survivor.Id,
            $"Merged in the enquiry from {absorbed.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC ({absorbed.FullName}).",
            actor,
            _clock.UtcNow));

        _dbContext.LeadActivities.Add(SystemEvent(
            absorbed.Id,
            "Merged into an earlier enquiry from the same person.",
            actor,
            _clock.UtcNow));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogMerged(_logger, absorbed.Id, survivor.Id, actor);
        return PipelineResult.Done(survivor.Id);
    }

    public async Task<PipelineResult> ConvertAsync(
        Guid leadId, ConversionRequest request, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lead = await _dbContext.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return PipelineResult.NotFound();
        }

        if (lead.Stage == LeadStage.Converted)
        {
            return PipelineResult.Conflict("ALREADY_CONVERTED", "This lead has already become a customer.");
        }

        if (lead.Stage is LeadStage.Spam or LeadStage.Merged)
        {
            return PipelineResult.Refused("STAGE_NOT_ALLOWED", "stage", $"A lead marked {lead.Stage} is not converted.");
        }

        var organisation = await ResolveOrganisationAsync(request, lead, actor, cancellationToken).ConfigureAwait(false);

        if (organisation is null)
        {
            // Neither an existing company nor a name to create one with. A customer record with a
            // blank name is worse than no customer record (REQ-LEAD-015).
            return PipelineResult.Refused(
                "ORGANISATION_REQUIRED", "organisationId", "Choose an existing company or give a name for a new one.");
        }

        var email = Trimmed(request.ContactEmail) ?? lead.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            return PipelineResult.Refused(
                "CONTACT_REQUIRED", "contactEmail", "A customer needs somebody to write to.");
        }

        var contact = await _dbContext.Contacts
            .FirstOrDefaultAsync(c => c.OrganisationId == organisation.Id && c.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (contact is null)
        {
            contact = new Contact
            {
                Id = Guid.NewGuid(),
                OrganisationId = organisation.Id,
                FullName = Trimmed(request.ContactFullName) ?? lead.FullName,
                Email = email,
                Phone = Trimmed(request.ContactPhone) ?? lead.Phone,
                JobTitle = Trimmed(request.ContactJobTitle),

                // The first person at a new company is the one to ring until somebody says
                // otherwise.
                IsPrimary = organisation.Contacts.Count == 0,
                CreatedBy = actor,
            };

            _dbContext.Contacts.Add(contact);
        }

        var from = lead.Stage;
        lead.Stage = LeadStage.Converted;
        lead.OrganisationId = organisation.Id;
        lead.ContactId = contact.Id;
        lead.ModifiedBy = actor;

        organisation.Status = OrganisationStatus.Customer;
        organisation.ModifiedBy = actor;

        _dbContext.LeadActivities.Add(StageChange(
            lead.Id, from, LeadStage.Converted, $"Converted to {organisation.DisplayName}.", actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineResult.Done(organisation.Id);
    }

    public async Task<PipelineResult> SetSpamAsync(Guid leadId, bool isSpam, string actor, CancellationToken cancellationToken)
    {
        var lead = await _dbContext.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken).ConfigureAwait(false);

        if (lead is null)
        {
            return PipelineResult.NotFound();
        }

        if (isSpam && lead.Stage == LeadStage.Spam)
        {
            return PipelineResult.Conflict("ALREADY_SPAM", "This lead is already marked as spam.");
        }

        if (!isSpam && lead.Stage != LeadStage.Spam)
        {
            return PipelineResult.Conflict("NOT_SPAM", "This lead is not marked as spam.");
        }

        var from = lead.Stage;

        if (isSpam)
        {
            lead.Stage = LeadStage.Spam;
        }
        else
        {
            // Restored to New, and the clock resumes from when the enquiry actually arrived rather
            // than from now. Somebody wrote in on Monday and was wrongly binned; the report should
            // show a week's delay, because that is what happened (REQ-LEAD-018).
            lead.Stage = LeadStage.New;
            lead.SlaDueAtUtc = _sla.FirstResponseDueUtc(lead.CreatedAtUtc);
        }

        lead.ModifiedBy = actor;

        _dbContext.LeadActivities.Add(StageChange(
            lead.Id,
            from,
            lead.Stage,
            isSpam ? "Marked as spam." : "Restored from spam; the response clock resumes from the original enquiry.",
            actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PipelineResult.Done(lead.Id);
    }

    private async Task<Organisation?> ResolveOrganisationAsync(
        ConversionRequest request, Lead lead, string actor, CancellationToken cancellationToken)
    {
        if (request.OrganisationId is { } id)
        {
            return await _dbContext.Organisations
                .Include(o => o.Contacts)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken).ConfigureAwait(false);
        }

        var name = Trimmed(request.OrganisationName) ?? Trimmed(lead.CompanyName);

        if (name is null)
        {
            return null;
        }

        var organisation = new Organisation
        {
            Id = Guid.NewGuid(),
            LegalName = name,
            DisplayName = name,
            Status = OrganisationStatus.Customer,
            CreatedBy = actor,
        };

        _dbContext.Organisations.Add(organisation);
        return organisation;
    }

    private LeadActivity StageChange(Guid leadId, LeadStage from, LeadStage to, string? reason, string actor) => new()
    {
        Id = Guid.NewGuid(),
        LeadId = leadId,
        ActivityType = LeadActivityType.StageChange,
        Direction = LeadActivityDirection.Internal,
        Body = reason,
        OccurredAtUtc = _clock.UtcNow,
        FromStage = from,
        ToStage = to,
        CreatedBy = actor,
    };

    private static LeadActivity SystemEvent(Guid leadId, string body, string actor, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        LeadId = leadId,
        ActivityType = LeadActivityType.SystemEvent,
        Direction = LeadActivityDirection.Internal,
        Body = body,
        OccurredAtUtc = at,
        CreatedBy = actor,
    };

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Lead {absorbed} merged into {survivor} by {actor}.")]
    private static partial void LogMerged(ILogger logger, Guid absorbed, Guid survivor, string actor);
}
