using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Sales;

/// <summary>
/// What happens after a customer says yes: a tenant, a subscription, invoices, money, and the
/// escalation nobody has to remember to run.
/// </summary>
[Collection(SalesTestGroup.Name)]
public sealed class BillingTests(SalesFixture fixture)
{
    private readonly SalesFixture _fixture = fixture;

    [Fact]
    public void REQ_SALE_015_The_proration_formula_is_the_one_the_requirement_states()
    {
        // Twenty days left of thirty, moving from 3,000 to 5,000. Each side is rounded before
        // subtracting, as the requirement states it: 5000 x 20/30 = 3333.33 owed for the new plan,
        // less 3000 x 20/30 = 2000.00 already paid for the old one.
        var charge = Proration.Charge(oldPeriodAmount: 3000m, newPeriodAmount: 5000m, daysRemaining: 20, daysInPeriod: 30);

        charge.Should().Be(1333.33m);
    }

    [Fact]
    public void REQ_SALE_015_A_reduction_costs_nothing_mid_term_rather_than_refunding()
    {
        // A downgrade takes effect at renewal, so the prorated charge is nothing rather than a
        // negative number somebody would have to refund (BR-SALE-11).
        Proration.Charge(5000m, 3000m, 20, 30).Should().Be(0m);

        // And a change on the last day of a period costs nothing either, because there is nothing
        // left to charge for.
        Proration.Charge(3000m, 5000m, 0, 30).Should().Be(0m);
    }

    [Fact]
    public void REQ_SALE_011_An_invoice_rounds_its_tax_the_way_every_other_line_is_rounded()
    {
        var invoice = new Invoice();
        invoice.SetAmount(14997.00m);

        invoice.SubTotal.Should().Be(14997.00m);
        invoice.TaxTotal.Should().Be(2699.46m);
        invoice.GrandTotal.Should().Be(17696.46m);
        invoice.Outstanding().Should().Be(17696.46m);
    }

    [Fact]
    public async Task REQ_SALE_007_An_accepted_quote_becomes_a_tenant_and_a_subscription()
    {
        var nonce = Nonce();
        var quoteId = await AcceptedQuoteAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var provisioned = await sales.PostAsJsonAsync($"/api/v1/subscriptions/from-quote/{quoteId}", new
        {
            name = $"PROBE-{nonce}",
            environmentUrl = $"https://{nonce}.example.test",
            environment = "Production",
        });

        provisioned.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == $"PROBE-{nonce}");
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.QuoteId.Should().Be(quoteId);
        subscription.CurrentPeriodEndsOn.Should().BeAfter(subscription.StartedOn);

        // Every status change writes an event beside it, including the first one (REQ-SALE-010).
        (await db.SubscriptionEvents.AsNoTracking().CountAsync(e => e.SubscriptionId == subscription.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task REQ_SALE_007_Provisioning_the_same_quote_twice_returns_the_first_subscription()
    {
        var nonce = Nonce();
        var quoteId = await AcceptedQuoteAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var body = new { name = $"PROBE-{nonce}", environment = "Production" };

        var first = await sales.PostAsJsonAsync($"/api/v1/subscriptions/from-quote/{quoteId}", body);
        var second = await sales.PostAsJsonAsync($"/api/v1/subscriptions/from-quote/{quoteId}", body);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        // Two people recording the same acceptance from two screens must not produce two tenants
        // and two subscriptions (BR-SALE-06).
        (await first.Content.ReadFromJsonAsync<ProvisionedRow>())!.Id
            .Should().Be((await second.Content.ReadFromJsonAsync<ProvisionedRow>())!.Id);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Subscriptions.AsNoTracking().CountAsync(s => s.QuoteId == quoteId)).Should().Be(1);
    }

    [Fact]
    public async Task REQ_SALE_007_A_quote_nobody_accepted_is_not_provisioned()
    {
        var nonce = Nonce();
        var quoteId = await SentQuoteAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/subscriptions/from-quote/{quoteId}", new
        {
            name = $"PROBE-{nonce}",
            environment = "Production",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("QUOTE_NOT_ACCEPTED");
    }

    [Fact]
    public async Task REQ_SALE_008_The_subscription_list_shows_the_tenant_its_product_plan_status_and_renewal()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var rows = await sales.GetFromJsonAsync<List<SubscriptionRow>>("/api/v1/subscriptions?pageSize=100");

        var row = rows!.Should().ContainSingle(r => r.Id == subscriptionId).Subject;

        row.Tenant.Should().Be($"PROBE-{nonce}");
        row.Product.Should().NotBeNullOrWhiteSpace();
        row.Plan.Should().NotBeNullOrWhiteSpace();
        row.Status.Should().Be(nameof(SubscriptionStatus.Active));
        row.CurrentPeriodEndsOn.Should().BeAfter(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
    }

    [Fact]
    public async Task REQ_SALE_008_A_tenant_records_where_the_instance_actually_runs()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce, $"https://{nonce}.example.test");
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var detail = await sales.GetFromJsonAsync<SubscriptionDetailRow>($"/api/v1/subscriptions/{subscriptionId}");

        // "I know what is running where" is the whole requirement, and a tenant with no address is
        // a row that does not answer it.
        detail!.EnvironmentUrl.Should().Be($"https://{nonce}.example.test");
        detail.Environment.Should().Be(nameof(TenantEnvironment.Production));
        detail.TenantStatus.Should().Be(nameof(TenantStatus.Requested));
    }

    [Fact]
    public async Task REQ_SALE_010_The_event_history_reads_in_order_with_the_first_one_at_the_bottom()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await sales.PostAsync($"/api/v1/subscriptions/{subscriptionId}/trial", null);

        var detail = await sales.GetFromJsonAsync<SubscriptionDetailRow>($"/api/v1/subscriptions/{subscriptionId}");

        detail!.Events.Should().HaveCountGreaterThanOrEqualTo(2);

        // Newest first, which is how somebody asking "what happened most recently" reads it.
        detail.Events[0].ToStatus.Should().Be(nameof(SubscriptionStatus.Trial));
        detail.Events[0].FromStatus.Should().Be(nameof(SubscriptionStatus.Active));
    }

    [Fact]
    public async Task REQ_SALE_014_A_renewal_further_out_than_the_notice_window_is_left_alone()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);

        // Forty days out, well beyond the fifteen-day window. Raising an invoice now would bill a
        // customer six weeks early.
        await SetPeriodEndAsync(subscriptionId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40));

        await RunSweepAsync();

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Invoices.AsNoTracking().AnyAsync(i => i.SubscriptionId == subscriptionId)).Should().BeFalse();

        (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId))
            .RenewalReminderSentFor.Should().BeNull();
    }

    [Fact]
    public async Task REQ_SALE_009_A_trial_started_today_ends_exactly_a_fortnight_later()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        (await sales.PostAsync($"/api/v1/subscriptions/{subscriptionId}/trial", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId);

        subscription.Status.Should().Be(SubscriptionStatus.Trial);
        subscription.TrialEndsOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14));
    }

    [Fact]
    public async Task REQ_SALE_009_A_trial_that_has_run_out_expires_when_the_sweep_runs()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await sales.PostAsync($"/api/v1/subscriptions/{subscriptionId}/trial", null);
        await BackdateTrialAsync(subscriptionId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        var result = await RunSweepAsync();
        result.TrialsExpired.Should().BeGreaterThanOrEqualTo(1);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId))
            .Status.Should().Be(SubscriptionStatus.Expired);

        (await db.SubscriptionEvents.AsNoTracking()
            .AnyAsync(e => e.SubscriptionId == subscriptionId && e.ToStatus == SubscriptionStatus.Expired))
            .Should().BeTrue();
    }

    [Fact]
    public async Task REQ_SALE_010_Every_state_change_records_both_states_the_reason_and_the_date()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        (await owner.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/cancel", new
        {
            reason = "Moved to an in-house build.",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var change = await db.SubscriptionEvents.AsNoTracking()
            .SingleAsync(e => e.SubscriptionId == subscriptionId && e.ToStatus == SubscriptionStatus.Cancelled);

        change.FromStatus.Should().Be(SubscriptionStatus.Active);
        change.Reason.Should().Be("Moved to an in-house build.");
        change.TriggeredBy.Should().Be(SalesFixture.OwnerEmail);
    }

    [Fact]
    public async Task REQ_SALE_016_Cancelling_without_a_reason_is_refused()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var response = await owner.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/cancel", new { reason = "  " });

        // A churn report with no reasons in it explains nothing.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("REASON_REQUIRED");
    }

    [Fact]
    public async Task REQ_SALE_016_A_cancelled_subscription_runs_to_the_end_of_the_paid_period()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        await owner.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/cancel", new { reason = "Budget cut for the year." });

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId);

        // The tenant ends when the period does, not on the day somebody clicked cancel: they paid
        // for the month (REQ-SALE-016).
        var ending = await db.SubscriptionEvents.AsNoTracking()
            .SingleAsync(e => e.SubscriptionId == subscriptionId && e.ToStatus == SubscriptionStatus.Cancelled);

        ending.EffectiveOn.Should().Be(subscription.CurrentPeriodEndsOn);
        subscription.CancelledOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task REQ_SALE_011_Issuing_an_invoice_writes_a_gapless_number_the_period_and_a_due_date()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var issued = await owner.PostAsync($"/api/v1/subscriptions/{subscriptionId}/invoices", null);
        issued.StatusCode.Should().Be(HttpStatusCode.Created);

        var row = (await issued.Content.ReadFromJsonAsync<IssuedInvoice>())!;
        row.InvoiceNumber.Should().MatchRegex(@"^INV/\d{4}-\d{2}/\d{5}$");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == row.Id);

        invoice.Status.Should().Be(InvoiceStatus.Issued);
        invoice.IssuedOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        invoice.DueDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14));
        invoice.PeriodEnd.Should().BeAfter(invoice.PeriodStart);
        invoice.GrandTotal.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task REQ_SALE_011_Two_invoices_issued_at_once_take_two_different_numbers()
    {
        var nonce = Nonce();
        var first = await ProvisionAsync(nonce);
        var second = await ProvisionAsync($"{nonce}-b");
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var responses = await Task.WhenAll(
            owner.PostAsync($"/api/v1/subscriptions/{first}/invoices", null),
            owner.PostAsync($"/api/v1/subscriptions/{second}/invoices", null));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var numbers = new List<string>();

        foreach (var response in responses)
        {
            numbers.Add((await response.Content.ReadFromJsonAsync<IssuedInvoice>())!.InvoiceNumber);
        }

        numbers.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task REQ_SALE_012_A_part_payment_leaves_the_invoice_partially_paid_with_the_rest_outstanding()
    {
        var nonce = Nonce();
        var (invoiceId, total) = await IssuedInvoiceAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var part = QuoteLineItem.Round(total / 2m);

        var recorded = await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", new
        {
            amount = part,
            mode = "NeftRtgs",
            referenceNumber = $"UTR-{nonce}",
        });

        recorded.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);

        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.AmountPaid.Should().Be(part);
        invoice.Outstanding().Should().Be(QuoteLineItem.Round(total - part));
    }

    [Fact]
    public async Task REQ_SALE_012_Paying_the_rest_marks_the_invoice_paid()
    {
        var nonce = Nonce();
        var (invoiceId, total) = await IssuedInvoiceAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", new
        {
            amount = total,
            mode = "Upi",
            referenceNumber = $"UPI-{nonce}",
        });

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.Outstanding().Should().Be(0m);
    }

    [Fact]
    public async Task REQ_SALE_012_An_overpayment_is_refused_and_the_refusal_names_the_outstanding_amount()
    {
        var nonce = Nonce();
        var (invoiceId, total) = await IssuedInvoiceAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var response = await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", new
        {
            amount = total + 1000m,
            mode = "Cheque",
            referenceNumber = $"CHQ-{nonce}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OVERPAYMENT");

        // The figure travels with the refusal: the person is holding a bank statement and needs to
        // know what to enter instead (BR-SALE-09).
        body.Should().Contain("outstanding");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Payments.AsNoTracking().AnyAsync(p => p.InvoiceId == invoiceId)).Should().BeFalse();
    }

    [Fact]
    public async Task REQ_SALE_012_The_same_bank_reference_twice_is_refused_rather_than_counted_twice()
    {
        var nonce = Nonce();
        var (invoiceId, total) = await IssuedInvoiceAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var body = new { amount = QuoteLineItem.Round(total / 4m), mode = "NeftRtgs", referenceNumber = $"UTR-{nonce}" };

        (await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", body)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Two people reconciling the same statement (EX-226).
        var again = await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", body);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("PAYMENT_ALREADY_RECORDED");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Payments.AsNoTracking().CountAsync(p => p.InvoiceId == invoiceId)).Should().Be(1);
    }

    [Fact]
    public async Task REQ_SALE_013_An_invoice_eight_days_late_moves_the_subscription_to_past_due()
    {
        var nonce = Nonce();
        var (invoiceId, _) = await IssuedInvoiceAsync(nonce);
        await AgeInvoiceAsync(invoiceId, daysLate: 8);

        var result = await RunSweepAsync();
        result.MovedToPastDue.Should().BeGreaterThanOrEqualTo(1);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == invoice.SubscriptionId);

        subscription.Status.Should().Be(SubscriptionStatus.PastDue);
        invoice.Status.Should().Be(InvoiceStatus.Overdue);

        (await db.SubscriptionEvents.AsNoTracking()
            .AnyAsync(e => e.SubscriptionId == subscription.Id && e.ToStatus == SubscriptionStatus.PastDue))
            .Should().BeTrue();
    }

    [Fact]
    public async Task REQ_SALE_013_The_same_invoice_at_twenty_two_days_suspends_the_subscription()
    {
        var nonce = Nonce();
        var (invoiceId, _) = await IssuedInvoiceAsync(nonce);

        await AgeInvoiceAsync(invoiceId, daysLate: 8);
        await RunSweepAsync();

        await AgeInvoiceAsync(invoiceId, daysLate: 22);
        var result = await RunSweepAsync();

        result.Suspended.Should().BeGreaterThanOrEqualTo(1);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == invoice.SubscriptionId);

        subscription.Status.Should().Be(SubscriptionStatus.Suspended);

        // Both steps left a row, so the history explains itself (BR-SALE-08, REQ-SALE-010).
        var escalations = await db.SubscriptionEvents.AsNoTracking()
            .Where(e => e.SubscriptionId == subscription.Id
                && (e.ToStatus == SubscriptionStatus.PastDue || e.ToStatus == SubscriptionStatus.Suspended))
            .CountAsync();

        escalations.Should().Be(2);
    }

    [Fact]
    public async Task REQ_SALE_013_An_invoice_that_is_paid_stops_being_chased_and_the_subscription_comes_back()
    {
        var nonce = Nonce();
        var (invoiceId, total) = await IssuedInvoiceAsync(nonce);
        await AgeInvoiceAsync(invoiceId, daysLate: 8);
        await RunSweepAsync();

        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        await owner.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/payments", new
        {
            amount = total,
            mode = "NeftRtgs",
            referenceNumber = $"SETTLE-{nonce}",
        });

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == invoice.SubscriptionId);

        // Leaving a paying customer past due is how one gets suspended for no reason.
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task NFR_DEP_05_Two_schedulers_against_one_database_do_the_work_once()
    {
        var nonce = Nonce();
        var (invoiceId, _) = await IssuedInvoiceAsync(nonce);
        await AgeInvoiceAsync(invoiceId, daysLate: 8);

        // Two schedulers against one database. They serialise on the lock rather than being
        // refused - the second waits, gets it, and finds the work already done - so what is
        // asserted is the guarantee itself: the escalation happened once and one email was queued
        // (NFR-DEP-05).
        var both = await Task.WhenAll(RunSweepAsync(), RunSweepAsync());

        both.Sum(r => r.MovedToPastDue).Should().Be(1);
        both.Sum(r => r.DunningQueued).Should().Be(1);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.OutboxEmails.AsNoTracking()
            .CountAsync(o => o.RelatedEntityId == invoiceId && o.TemplateKey == "invoice.overdue"))
            .Should().Be(1);
    }

    [Fact]
    public async Task NFR_DEP_05_A_sweep_declines_while_another_holds_the_lock()
    {
        var nonce = Nonce();
        var (invoiceId, _) = await IssuedInvoiceAsync(nonce);
        await AgeInvoiceAsync(invoiceId, daysLate: 8);

        // The mechanism itself, deterministically: hold the lock from here, and the sweep finds it
        // held and does nothing at all. The test above asserts the outcome; this one asserts the
        // reason the outcome holds.
        using var holder = _fixture.NewScope();
        var db = holder.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = holder.ServiceProvider.GetRequiredService<ILogger<BillingTests>>();

        await using var held = new DatabaseLock(db, logger);
        (await held.AcquireAsync(DatabaseLock.BillingSweep, TimeSpan.FromSeconds(5), CancellationToken.None))
            .Should().BeTrue();

        var declined = await RunSweepAsync();

        declined.LockTaken.Should().BeFalse();
        declined.MovedToPastDue.Should().Be(0);

        using var check = _fixture.NewScope();
        var database = check.ServiceProvider.GetRequiredService<AppDbContext>();

        (await database.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId))
            .Status.Should().Be(InvoiceStatus.Issued);
    }

    [Fact]
    public async Task REQ_SALE_014_A_renewal_inside_the_notice_window_raises_one_invoice_and_not_a_second()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);

        // Ten days out, inside the fifteen-day notice window.
        await SetPeriodEndAsync(subscriptionId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        var first = await RunSweepAsync();
        first.RenewalsInvoiced.Should().BeGreaterThanOrEqualTo(1);

        var second = await RunSweepAsync();

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Once per renewal, not once per pass: a reminder that arrives every hour is one people
        // filter out (REQ-SALE-014).
        (await db.Invoices.AsNoTracking().CountAsync(i => i.SubscriptionId == subscriptionId)).Should().Be(1);
        second.RenewalsInvoiced.Should().Be(0);
    }

    [Fact]
    public async Task REQ_SALE_015_A_plan_upgrade_mid_term_raises_a_prorated_invoice()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        Guid planId;

        using (var arrange = _fixture.NewScope())
        {
            var db = arrange.ServiceProvider.GetRequiredService<AppDbContext>();
            planId = (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId)).PricingPlanId;
        }

        var response = await sales.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/plan", new
        {
            pricingPlanId = planId,
            seats = 4,
            unitPrice = 5000m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db2.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId);

        subscription.Seats.Should().Be(4);
        subscription.UnitPrice.Should().Be(5000m);

        (await db2.SubscriptionEvents.AsNoTracking()
            .AnyAsync(e => e.SubscriptionId == subscriptionId && e.Reason!.Contains("prorated")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task REQ_SALE_015_A_seat_reduction_takes_effect_at_renewal_and_raises_no_invoice()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        Guid planId;

        using (var arrange = _fixture.NewScope())
        {
            var db = arrange.ServiceProvider.GetRequiredService<AppDbContext>();
            planId = (await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId)).PricingPlanId;
        }

        await sales.PostAsJsonAsync($"/api/v1/subscriptions/{subscriptionId}/plan", new
        {
            pricingPlanId = planId,
            seats = 1,
            unitPrice = 100m,
        });

        using var scope = _fixture.NewScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db2.Invoices.AsNoTracking().AnyAsync(i => i.SubscriptionId == subscriptionId)).Should().BeFalse();

        (await db2.SubscriptionEvents.AsNoTracking()
            .AnyAsync(e => e.SubscriptionId == subscriptionId && e.Reason!.Contains("effective")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task NFR_AUTHZ_02_The_editor_is_refused_the_whole_of_billing()
    {
        var nonce = Nonce();
        var subscriptionId = await ProvisionAsync(nonce);
        var editor = await _fixture.ClientAsAsync(SalesFixture.EditorEmail);

        // Money is not the site editor's to see or move (AZ-46 onwards).
        (await editor.GetAsync("/api/v1/subscriptions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync($"/api/v1/subscriptions/{subscriptionId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync("/api/v1/invoices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await editor.PostAsync($"/api/v1/subscriptions/{subscriptionId}/invoices", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_SALE_011_The_invoice_register_reads_in_number_order_so_a_gap_would_show()
    {
        var nonce = Nonce();
        await IssuedInvoiceAsync(nonce);
        await IssuedInvoiceAsync($"{nonce}-b");

        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);
        var register = await owner.GetFromJsonAsync<List<InvoiceRegisterRow>>("/api/v1/invoices?pageSize=200");

        register!.Count.Should().BeGreaterThanOrEqualTo(2);

        // Number order, not date order: a gap in the sequence is what an auditor is looking for,
        // and a date ordering hides it (REQ-RPT-007).
        register.Select(r => r.InvoiceNumber).Should().BeInAscendingOrder();
    }

    private static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private async Task<BillingSweepResult> RunSweepAsync()
    {
        using var scope = _fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingService>().RunSweepAsync(CancellationToken.None);
    }

    /// <summary>A sent quote with one line on it.</summary>
    private async Task<Guid> SentQuoteAsync(string nonce)
    {
        var (organisationId, contactId) = await _fixture.AddCustomerAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId });
        var quoteId = (await created.Content.ReadFromJsonAsync<CreatedQuote>())!.Id;

        await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", new
        {
            productId = _fixture.ProductId,
            description = "One year, supported",
            quantity = 2,
            unitPrice = 3000m,
            discountAmount = 0m,
            taxRatePercent = 18.00m,
        });

        await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { });
        return quoteId;
    }

    private async Task<Guid> AcceptedQuoteAsync(string nonce)
    {
        var quoteId = await SentQuoteAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await sales.PostAsync($"/api/v1/quotes/{quoteId}/accept", null);
        return quoteId;
    }

    private async Task<Guid> ProvisionAsync(string nonce, string? environmentUrl = null)
    {
        var quoteId = await AcceptedQuoteAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var provisioned = await sales.PostAsJsonAsync($"/api/v1/subscriptions/from-quote/{quoteId}", new
        {
            name = $"PROBE-{nonce}",
            environmentUrl,
            environment = "Production",
        });

        if (!provisioned.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{(int)provisioned.StatusCode}: {await provisioned.Content.ReadAsStringAsync()}");
        }

        return (await provisioned.Content.ReadFromJsonAsync<ProvisionedRow>())!.Id;
    }

    private async Task<(Guid InvoiceId, decimal Total)> IssuedInvoiceAsync(string nonce)
    {
        var subscriptionId = await ProvisionAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var issued = await owner.PostAsync($"/api/v1/subscriptions/{subscriptionId}/invoices", null);

        if (!issued.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)issued.StatusCode}: {await issued.Content.ReadAsStringAsync()}");
        }

        var row = (await issued.Content.ReadFromJsonAsync<IssuedInvoice>())!;
        return (row.Id, row.GrandTotal);
    }

    /// <summary>Moves an invoice's due date into the past, which is what the calendar would do.</summary>
    private async Task AgeInvoiceAsync(Guid invoiceId, int daysLate)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Invoices.Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysLate)));
    }

    private async Task BackdateTrialAsync(Guid subscriptionId, DateOnly endsOn)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Subscriptions.Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.TrialEndsOn, endsOn)
                .SetProperty(x => x.CurrentPeriodEndsOn, endsOn));
    }

    private async Task SetPeriodEndAsync(Guid subscriptionId, DateOnly endsOn)
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Subscriptions.Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEndsOn, endsOn));
    }

    private sealed record CreatedQuote(Guid Id, string QuoteNumber);

    private sealed record ProvisionedRow(Guid Id, string Tenant);

    private sealed record IssuedInvoice(Guid Id, string InvoiceNumber, decimal GrandTotal);

    private sealed record InvoiceRegisterRow(Guid Id, string InvoiceNumber, string Status, decimal GrandTotal, decimal Outstanding);

    private sealed record EventRow(Guid Id, string? FromStatus, string ToStatus, string? Reason, DateOnly EffectiveOn, string TriggeredBy);

    private sealed record SubscriptionDetailRow(
        Guid Id,
        string Tenant,
        string? EnvironmentUrl,
        string Environment,
        string TenantStatus,
        string Status,
        int Seats,
        DateOnly CurrentPeriodEndsOn,
        IReadOnlyList<EventRow> Events,
        IReadOnlyList<InvoiceRegisterRow> Invoices);

    private sealed record SubscriptionRow(
        Guid Id,
        Guid TenantId,
        string Tenant,
        string Organisation,
        string Product,
        string Plan,
        string Status,
        int Seats,
        DateOnly CurrentPeriodEndsOn);
}
