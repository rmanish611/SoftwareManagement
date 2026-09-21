using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Notifications;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Notifications;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Sales;

/// <summary>
/// Tenants, subscriptions, invoices and the money against them.
///
/// Every status change on a subscription writes an event beside it, in the same transaction. A
/// status column says where a subscription is; only the events say how it got there, and "why was
/// I suspended" is a question a customer will ask months later (REQ-SALE-010).
/// </summary>
public sealed partial class BillingService(
    AppDbContext dbContext,
    INumberAllocator numbers,
    IEmailOutbox outbox,
    ISystemSettings settings,
    IClock clock,
    ILogger<BillingService> logger) : IBillingService
{
    /// <summary>How many of each kind one sweep pass handles, so a backlog cannot make it run long.</summary>
    public const int BatchSize = 50;

    /// <summary>A renewal invoice is raised this many days before the period ends (REQ-SALE-014).</summary>
    public const int RenewalNoticeDays = 15;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly INumberAllocator _numbers = numbers;
    private readonly IEmailOutbox _outbox = outbox;
    private readonly ISystemSettings _settings = settings;
    private readonly IClock _clock = clock;
    private readonly ILogger<BillingService> _logger = logger;

    public async Task<BillingResult> ProvisionFromQuoteAsync(
        Guid quoteId, NewTenant tenant, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var quote = await _dbContext.Quotes
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return BillingResult.NotFound();
        }

        // Idempotent: two people recording the same acceptance from two screens must not produce
        // two tenants and two subscriptions (BR-SALE-06).
        var existing = await _dbContext.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.QuoteId == quoteId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return BillingResult.Done(existing.Id);
        }

        if (quote.Status != QuoteStatus.Accepted)
        {
            return BillingResult.Refused(
                "QUOTE_NOT_ACCEPTED", "quoteId", "A quote becomes a subscription once the customer has accepted it.");
        }

        var line = quote.Lines.OrderBy(l => l.SortOrder).FirstOrDefault();

        if (line is null)
        {
            return BillingResult.Refused("QUOTE_EMPTY", "quoteId", "That quote has no lines to provision from.");
        }

        if (string.IsNullOrWhiteSpace(tenant.Name))
        {
            return BillingResult.Refused("TENANT_NAME_REQUIRED", "name", "A tenant needs a name somebody can recognise.");
        }

        var plan = line.PricingPlanId ?? await _dbContext.PricingPlans
            .Where(p => p.ProductId == line.ProductId && p.IsPublished)
            .OrderBy(p => p.SortOrder)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (plan is null)
        {
            return BillingResult.Refused(
                "PLAN_REQUIRED", "quoteId", "That quote line names no plan and the product has no published one.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var row = new Tenant
        {
            Id = Guid.NewGuid(),
            OrganisationId = quote.OrganisationId,
            ProductId = line.ProductId,
            Name = tenant.Name.Trim(),
            EnvironmentUrl = tenant.EnvironmentUrl,
            Environment = tenant.Environment,
            Status = TenantStatus.Requested,
            Notes = tenant.Notes,
            CreatedBy = actor,
        };

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = row.Id,
            PricingPlanId = plan.Value,
            QuoteId = quote.Id,

            // Active rather than Trial: somebody has signed a quote. A trial is started explicitly
            // and has its own clock (BR-SALE-07).
            Status = SubscriptionStatus.Active,
            Seats = line.Quantity,
            UnitPrice = line.UnitPrice,
            Currency = quote.Currency,
            BillingPeriod = BillingPeriod.Monthly,
            StartedOn = today,
            CreatedBy = actor,
        };

        subscription.CurrentPeriodEndsOn = subscription.NextPeriodEnd(today);

        _dbContext.Tenants.Add(row);
        _dbContext.Subscriptions.Add(subscription);
        _dbContext.SubscriptionEvents.Add(Event(subscription.Id, null, SubscriptionStatus.Active, "Provisioned from " + quote.QuoteNumber, today, actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BillingResult.Done(subscription.Id, row.Name);
    }

    public async Task<BillingResult> StartTrialAsync(Guid subscriptionId, string actor, CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(subscriptionId, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            return BillingResult.NotFound();
        }

        if (subscription.Status != SubscriptionStatus.Active && subscription.Status != SubscriptionStatus.Trial)
        {
            return BillingResult.Conflict(
                "SUBSCRIPTION_NOT_STARTABLE", $"A {subscription.Status.ToString().ToLowerInvariant()} subscription is not put on trial.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var from = subscription.Status;

        subscription.Status = SubscriptionStatus.Trial;
        subscription.StartedOn = today;

        // Exactly a fortnight, and the end date is stored rather than computed later, so moving the
        // constant afterwards cannot silently extend a trial somebody is already on (BR-SALE-07).
        subscription.TrialEndsOn = today.AddDays(Subscription.TrialDays);
        subscription.CurrentPeriodEndsOn = subscription.TrialEndsOn.Value;
        subscription.ModifiedBy = actor;

        _dbContext.SubscriptionEvents.Add(Event(
            subscription.Id, from, SubscriptionStatus.Trial, $"{Subscription.TrialDays}-day trial started", today, actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BillingResult.Done(subscription.Id);
    }

    public async Task<BillingResult> IssueInvoiceAsync(Guid subscriptionId, string actor, CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(subscriptionId, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            return BillingResult.NotFound();
        }

        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            return BillingResult.Conflict(
                "SUBSCRIPTION_ENDED", $"A {subscription.Status.ToString().ToLowerInvariant()} subscription is not invoiced.");
        }

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstAsync(t => t.Id == subscription.TenantId, cancellationToken).ConfigureAwait(false);

        var invoice = await CreateInvoiceAsync(
            subscription,
            tenant.OrganisationId,
            subscription.PeriodAmount(),
            $"{tenant.Name} — {subscription.BillingPeriod.ToString().ToLowerInvariant()} subscription",
            subscription.CurrentPeriodEndsOn,
            actor,
            cancellationToken).ConfigureAwait(false);

        return BillingResult.Done(invoice.Id, invoice.InvoiceNumber, invoice.GrandTotal);
    }

    public async Task<BillingResult> RecordPaymentAsync(
        Guid invoiceId, NewPayment payment, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var invoice = await _dbContext.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken).ConfigureAwait(false);

        if (invoice is null)
        {
            return BillingResult.NotFound();
        }

        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
        {
            return BillingResult.Conflict(
                "INVOICE_NOT_PAYABLE", $"A {invoice.Status.ToString().ToLowerInvariant()} invoice is not paid against.");
        }

        if (payment.Amount <= 0)
        {
            return BillingResult.Refused("AMOUNT_INVALID", "amount", "A payment is an amount greater than nothing.");
        }

        if (string.IsNullOrWhiteSpace(payment.ReferenceNumber))
        {
            return BillingResult.Refused(
                "REFERENCE_REQUIRED", "referenceNumber", "A payment needs the bank's reference, so it can be reconciled.");
        }

        var reference = payment.ReferenceNumber.Trim();

        // The same transfer entered twice by two people reconciling one statement (EX-226). Refusing
        // it is what stops an invoice looking paid twice.
        if (invoice.Payments.Any(p => string.Equals(p.ReferenceNumber, reference, StringComparison.OrdinalIgnoreCase)))
        {
            return BillingResult.Conflict("PAYMENT_ALREADY_RECORDED", $"Reference {reference} is already against this invoice.");
        }

        var outstanding = invoice.Outstanding();

        if (payment.Amount > outstanding)
        {
            // Naming the figure, because the person is holding a bank statement and needs to know
            // what to enter instead (BR-SALE-09).
            return BillingResult.Refused(
                "OVERPAYMENT",
                "amount",
                $"That is more than the {outstanding:0.00} outstanding on this invoice.",
                outstanding);
        }

        _dbContext.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Amount = payment.Amount,
            Mode = payment.Mode,
            ReferenceNumber = reference,
            ReceivedOn = payment.ReceivedOn,
            RecordedByUserId = payment.RecordedByUserId,
            Notes = payment.Notes,
            CreatedBy = actor,
        });

        invoice.AmountPaid = QuoteLineItem.Round(invoice.AmountPaid + payment.Amount);
        invoice.Status = invoice.Outstanding() <= 0m ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        invoice.ModifiedBy = actor;

        // Paying up brings a past-due subscription back. Leaving it past due after the money
        // arrived is how a paying customer gets suspended.
        if (invoice.Status == InvoiceStatus.Paid && invoice.SubscriptionId is { } id)
        {
            var subscription = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

            if (subscription is not null && subscription.Status is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended)
            {
                var unpaid = await _dbContext.Invoices
                    .CountAsync(i => i.SubscriptionId == id && i.Id != invoice.Id
                        && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue),
                        cancellationToken).ConfigureAwait(false);

                if (unpaid == 0)
                {
                    var from = subscription.Status;
                    subscription.Status = SubscriptionStatus.Active;
                    subscription.ModifiedBy = actor;

                    _dbContext.SubscriptionEvents.Add(Event(
                        subscription.Id, from, SubscriptionStatus.Active, $"Invoice {invoice.InvoiceNumber} paid in full",
                        payment.ReceivedOn, actor));
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BillingResult.Done(invoice.Id, invoice.InvoiceNumber, invoice.Outstanding());
    }

    public async Task<BillingResult> ChangePlanAsync(
        Guid subscriptionId, PlanChange change, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var subscription = await LoadAsync(subscriptionId, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            return BillingResult.NotFound();
        }

        if (!subscription.IsBillable() && subscription.Status != SubscriptionStatus.Trial)
        {
            return BillingResult.Conflict(
                "SUBSCRIPTION_ENDED", $"A {subscription.Status.ToString().ToLowerInvariant()} subscription is not changed.");
        }

        if (change.Seats <= 0)
        {
            return BillingResult.Refused("SEATS_INVALID", "seats", "A subscription needs at least one seat.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var oldAmount = subscription.PeriodAmount();
        var newAmount = QuoteLineItem.Round(change.Seats * change.UnitPrice);

        var daysRemaining = Proration.DaysBetween(today, subscription.CurrentPeriodEndsOn);
        var daysInPeriod = Proration.DaysBetween(
            subscription.NextPeriodEnd(subscription.CurrentPeriodEndsOn).AddMonths(-1), subscription.CurrentPeriodEndsOn);

        if (daysInPeriod <= 0)
        {
            daysInPeriod = 30;
        }

        var charge = Proration.Charge(oldAmount, newAmount, daysRemaining, daysInPeriod);

        var reduction = newAmount < oldAmount;

        if (reduction)
        {
            // A decrease takes effect at renewal rather than refunding mid-term (BR-SALE-11). The
            // new figures are applied and the customer keeps what they paid for until the period
            // ends, which is what "takes effect at renewal" means in practice.
            _dbContext.SubscriptionEvents.Add(Event(
                subscription.Id, subscription.Status, subscription.Status,
                $"Seats or plan reduced; effective {subscription.CurrentPeriodEndsOn:yyyy-MM-dd}", today, actor));
        }
        else
        {
            _dbContext.SubscriptionEvents.Add(Event(
                subscription.Id, subscription.Status, subscription.Status,
                $"Plan changed mid-term; prorated charge {charge:0.00}", today, actor));
        }

        subscription.PricingPlanId = change.PricingPlanId;
        subscription.Seats = change.Seats;
        subscription.UnitPrice = change.UnitPrice;
        subscription.ModifiedBy = actor;

        Guid? invoiceId = null;
        string? invoiceNumber = null;

        if (!reduction && charge > 0m)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstAsync(t => t.Id == subscription.TenantId, cancellationToken).ConfigureAwait(false);

            var invoice = await CreateInvoiceAsync(
                subscription,
                tenant.OrganisationId,
                charge,
                $"{tenant.Name} — plan change for {daysRemaining} of {daysInPeriod} days",
                subscription.CurrentPeriodEndsOn,
                actor,
                cancellationToken).ConfigureAwait(false);

            invoiceId = invoice.Id;
            invoiceNumber = invoice.InvoiceNumber;
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return BillingResult.Done(invoiceId ?? subscription.Id, invoiceNumber, charge);
    }

    public async Task<BillingResult> CancelAsync(
        Guid subscriptionId, string reason, string actor, CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(subscriptionId, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            return BillingResult.NotFound();
        }

        if (subscription.Status == SubscriptionStatus.Cancelled)
        {
            return BillingResult.Conflict("ALREADY_CANCELLED", "That subscription is already cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return BillingResult.Refused("REASON_REQUIRED", "reason", "Say why. A churn report with no reasons explains nothing.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var from = subscription.Status;

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledOn = today;
        subscription.CancelReason = reason.Trim();
        subscription.ModifiedBy = actor;

        // The customer paid to the end of the period, so they keep it. The tenant ends when the
        // period does, not today (REQ-SALE-016).
        var tenant = await _dbContext.Tenants
            .FirstAsync(t => t.Id == subscription.TenantId, cancellationToken).ConfigureAwait(false);

        tenant.Status = TenantStatus.Ended;
        tenant.ModifiedBy = actor;

        _dbContext.SubscriptionEvents.Add(Event(
            subscription.Id, from, SubscriptionStatus.Cancelled, reason.Trim(), subscription.CurrentPeriodEndsOn, actor));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BillingResult.Done(subscription.Id);
    }

    public async Task<BillingSweepResult> RunSweepAsync(CancellationToken cancellationToken)
    {
        // Every scheduled job takes the lock first. A second instance finds it held, does nothing,
        // and says so - which is what stops two dunning emails for one invoice (NFR-DEP-05).
        await using var guard = new DatabaseLock(_dbContext, _logger);

        if (!await guard.AcquireAsync(DatabaseLock.BillingSweep, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
        {
            LogSweepLockNotTaken(_logger);
            return new BillingSweepResult(0, 0, 0, 0, 0, LockTaken: false);
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var notifyTo = _settings.Value("notify.ownerEmail");

        var trials = await ExpireTrialsAsync(today, cancellationToken).ConfigureAwait(false);
        var (pastDue, suspended, dunned) = await EscalateUnpaidAsync(today, notifyTo, cancellationToken).ConfigureAwait(false);
        var renewals = await RaiseRenewalsAsync(today, notifyTo, cancellationToken).ConfigureAwait(false);

        return new BillingSweepResult(trials, pastDue, suspended, renewals, dunned, LockTaken: true);
    }

    private async Task<int> ExpireTrialsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var finished = await _dbContext.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Trial && s.TrialEndsOn != null && s.TrialEndsOn < today)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var subscription in finished)
        {
            subscription.Status = SubscriptionStatus.Expired;
            subscription.ModifiedBy = "sweep";

            _dbContext.SubscriptionEvents.Add(Event(
                subscription.Id, SubscriptionStatus.Trial, SubscriptionStatus.Expired,
                "The trial ended with no plan confirmed", today, "sweep"));
        }

        if (finished.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return finished.Count;
    }

    private async Task<(int PastDue, int Suspended, int Dunned)> EscalateUnpaidAsync(
        DateOnly today, string? notifyTo, CancellationToken cancellationToken)
    {
        var overdue = await _dbContext.Invoices
            .Include(i => i.Subscription)
            .Where(i => i.DueDate != null && i.DueDate < today
                && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue))
            .OrderBy(i => i.DueDate)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var pastDue = 0;
        var suspended = 0;
        var dunned = 0;

        foreach (var invoice in overdue)
        {
            var daysLate = today.DayNumber - invoice.DueDate!.Value.DayNumber;

            if (invoice.Status != InvoiceStatus.Overdue && invoice.Status != InvoiceStatus.PartiallyPaid)
            {
                invoice.Status = InvoiceStatus.Overdue;
                invoice.ModifiedBy = "sweep";
            }

            var subscription = invoice.Subscription;

            if (subscription is null)
            {
                continue;
            }

            // Seven days late moves it to past due, twenty-one to suspended, and each writes its
            // own event (BR-SALE-08).
            var target = daysLate > Invoice.SuspendAfterDays
                ? SubscriptionStatus.Suspended
                : daysLate > Invoice.PastDueAfterDays
                    ? SubscriptionStatus.PastDue
                    : subscription.Status;

            if (target != subscription.Status && subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
            {
                var from = subscription.Status;
                subscription.Status = target;
                subscription.ModifiedBy = "sweep";

                _dbContext.SubscriptionEvents.Add(Event(
                    subscription.Id, from, target, $"Invoice {invoice.InvoiceNumber} is {daysLate} days late", today, "sweep"));

                if (target == SubscriptionStatus.Suspended)
                {
                    suspended++;
                }
                else
                {
                    pastDue++;
                }

                if (!string.IsNullOrWhiteSpace(notifyTo))
                {
                    var queued = await _outbox.QueueAsync(
                        EmailTemplate.InvoiceOverdue,
                        notifyTo,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["invoiceNumber"] = invoice.InvoiceNumber,
                            ["days"] = daysLate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["amount"] = invoice.Outstanding().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                            ["status"] = target.ToString(),
                        },
                        correlationId: invoice.Id,
                        relatedEntityType: nameof(Invoice),
                        relatedEntityId: invoice.Id,
                        cancellationToken).ConfigureAwait(false);

                    if (queued.Queued)
                    {
                        dunned++;
                    }
                }
            }
        }

        if (overdue.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return (pastDue, suspended, dunned);
    }

    private async Task<int> RaiseRenewalsAsync(DateOnly today, string? notifyTo, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(RenewalNoticeDays);

        var due = await _dbContext.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active
                && s.CurrentPeriodEndsOn <= horizon
                && (s.RenewalReminderSentFor == null || s.RenewalReminderSentFor != s.CurrentPeriodEndsOn))
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var raised = 0;

        foreach (var subscription in due)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstAsync(t => t.Id == subscription.TenantId, cancellationToken).ConfigureAwait(false);

            var periodEnd = subscription.NextPeriodEnd(subscription.CurrentPeriodEndsOn);

            await CreateInvoiceAsync(
                subscription,
                tenant.OrganisationId,
                subscription.PeriodAmount(),
                $"{tenant.Name} — renewal to {periodEnd:yyyy-MM-dd}",
                periodEnd,
                "sweep",
                cancellationToken).ConfigureAwait(false);

            // Stamped with the renewal it was for, so the next pass reminds about the next one
            // rather than this one again (REQ-SALE-014).
            subscription.RenewalReminderSentFor = subscription.CurrentPeriodEndsOn;
            subscription.ModifiedBy = "sweep";

            if (!string.IsNullOrWhiteSpace(notifyTo))
            {
                await _outbox.QueueAsync(
                    EmailTemplate.SubscriptionRenewing,
                    notifyTo,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tenant"] = tenant.Name,
                        ["renewsOn"] = subscription.CurrentPeriodEndsOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        ["amount"] = subscription.PeriodAmount().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    },
                    correlationId: subscription.Id,
                    relatedEntityType: nameof(Subscription),
                    relatedEntityId: subscription.Id,
                    cancellationToken).ConfigureAwait(false);
            }

            raised++;
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return raised;
    }

    /// <summary>
    /// Writes one invoice, with its number allocated inside the same transaction that saves it, so
    /// a failure takes the number back with it (BR-SALE-10).
    /// </summary>
    private async Task<Invoice> CreateInvoiceAsync(
        Subscription subscription,
        Guid organisationId,
        decimal subTotal,
        string description,
        DateOnly periodEnd,
        string actor,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var number = await _numbers
                .NextAsync(NumberSequence.Invoice, "INV", today, cancellationToken).ConfigureAwait(false);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = number,
                SubscriptionId = subscription.Id,
                OrganisationId = organisationId,
                Status = InvoiceStatus.Issued,
                IssuedOn = today,
                DueDate = today.AddDays(Subscription.PaymentTermDays),
                PeriodStart = today,
                PeriodEnd = periodEnd,
                Description = description,
                Currency = subscription.Currency,
                CreatedBy = actor,
            };

            invoice.SetAmount(subTotal);

            _dbContext.Invoices.Add(invoice);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return invoice;
        }).ConfigureAwait(false);
    }

    private Task<Subscription?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    private static SubscriptionEvent Event(
        Guid subscriptionId, SubscriptionStatus? from, SubscriptionStatus to, string? reason, DateOnly effectiveOn, string actor) => new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            EffectiveOn = effectiveOn,
            TriggeredBy = actor,
            CreatedBy = actor,
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "The billing sweep lock is held elsewhere; this pass did nothing.")]
    private static partial void LogSweepLockNotTaken(ILogger logger);
}
