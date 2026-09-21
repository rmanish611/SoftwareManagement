using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Sales;

/// <summary>
/// Quoting: the money, the numbering, and who may change a figure somebody has already been shown.
/// </summary>
[Collection(SalesTestGroup.Name)]
public sealed class QuoteTests(SalesFixture fixture)
{
    private readonly SalesFixture _fixture = fixture;

    [Fact]
    public async Task REQ_SALE_003_Tax_is_rounded_per_line_and_the_total_is_the_sum_of_the_rounded_lines()
    {
        // The example from the phase plan: 14,997.00 and 2,500.00 at 18 percent. 2699.46 and
        // 450.00 in tax, and the grand total is those lines plus those taxes - not 18 percent of
        // 17,497.00 computed once at the end (BR-SALE-03).
        var first = new QuoteLineItem { Quantity = 1, UnitPrice = 14997.00m, TaxRatePercent = 18.00m };
        var second = new QuoteLineItem { Quantity = 1, UnitPrice = 2500.00m, TaxRatePercent = 18.00m };

        first.Recalculate();
        second.Recalculate();

        first.LineTotal.Should().Be(14997.00m);
        first.TaxAmount.Should().Be(2699.46m);
        second.TaxAmount.Should().Be(450.00m);

        var grandTotal = first.LineTotal + first.TaxAmount + second.LineTotal + second.TaxAmount;
        grandTotal.Should().Be(20646.46m);
    }

    [Fact]
    public void REQ_SALE_003_Money_rounds_half_away_from_zero_rather_than_to_even()
    {
        // .NET rounds half to even by default, so 0.125 would become 0.12 and an invoice would
        // disagree with a hand calculator by a paisa. That is a query from a customer.
        QuoteLineItem.Round(0.125m).Should().Be(0.13m);
        QuoteLineItem.Round(0.135m).Should().Be(0.14m);
    }

    [Fact]
    public void REQ_SALE_001_A_financial_year_runs_from_April_and_is_named_for_both_years()
    {
        NumberSequence.FinancialYearOf(new DateOnly(2026, 4, 1)).Should().Be("2026-27");
        NumberSequence.FinancialYearOf(new DateOnly(2027, 3, 31)).Should().Be("2026-27");

        // The first of April is a different year from the thirty-first of March, which is the whole
        // point of an Indian financial year.
        NumberSequence.FinancialYearOf(new DateOnly(2027, 4, 1)).Should().Be("2027-28");
    }

    [Fact]
    public async Task REQ_SALE_001_Creating_a_quote_writes_a_row_carrying_a_financial_year_number()
    {
        var nonce = Nonce();
        var (organisationId, contactId) = await _fixture.AddCustomerAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var row = (await created.Content.ReadFromJsonAsync<CreatedQuote>())!;
        row.QuoteNumber.Should().MatchRegex(@"^Q/\d{4}-\d{2}/\d{5}$");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var quote = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == row.Id);
        quote.Status.Should().Be(QuoteStatus.Draft);
        quote.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task REQ_SALE_001_Twenty_quotes_created_at_once_get_a_contiguous_block_with_no_gap_and_no_duplicate()
    {
        var nonce = Nonce();
        var (organisationId, contactId) = await _fixture.AddCustomerAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        // Twenty at once, which is what `MAX(number) + 1` cannot survive: two requests read the
        // same maximum and write the same number (BR-SALE-01).
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var numbers = new List<string>();

        foreach (var response in responses)
        {
            numbers.Add((await response.Content.ReadFromJsonAsync<CreatedQuote>())!.QuoteNumber);
        }

        numbers.Should().OnlyHaveUniqueItems();

        var values = numbers.Select(n => int.Parse(n[^5..], System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();

        // Contiguous: the twenty values run from the lowest to the lowest plus nineteen, with
        // nothing missing in between.
        values.Should().BeEquivalentTo(Enumerable.Range(values[0], 20));
    }

    [Fact]
    public async Task REQ_SALE_002_A_quote_addressed_to_somebody_at_another_company_is_refused()
    {
        var nonce = Nonce();
        var (organisationId, _) = await _fixture.AddCustomerAsync(nonce);
        var (_, otherContactId) = await _fixture.AddCustomerAsync($"{nonce}-other");

        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId = otherContactId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CONTACT_NOT_AT_ORGANISATION");
    }

    [Fact]
    public async Task REQ_SALE_003_A_quote_s_totals_are_the_sum_of_its_rounded_lines()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 1, 14997.00m);
        await AddLineAsync(sales, quoteId, 1, 2500.00m);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quote = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);

        quote.SubTotal.Should().Be(17497.00m);
        quote.TaxTotal.Should().Be(3149.46m);
        quote.GrandTotal.Should().Be(20646.46m);
    }

    [Fact]
    public async Task REQ_SALE_004_A_sales_user_applying_a_twenty_percent_discount_is_refused_with_approval_required()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 10000m, discount: 2000m));

        // 403, not 422: the body is fine, the person is not allowed to send it (BR-SALE-04).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("APPROVAL_REQUIRED");
        body.Should().Contain("thresholdPercent");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.QuoteLineItems.AsNoTracking().AnyAsync(l => l.QuoteId == quoteId)).Should().BeFalse();
    }

    [Fact]
    public async Task REQ_SALE_004_A_discount_inside_the_limit_is_applied_without_anybody_being_asked()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 10000m, discount: 1500m));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var line = await db.QuoteLineItems.AsNoTracking().SingleAsync(l => l.QuoteId == quoteId);
        line.LineTotal.Should().Be(8500.00m);
        line.TaxAmount.Should().Be(1530.00m);

        (await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId)).ApprovedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task REQ_SALE_004_The_owner_may_apply_the_same_discount_and_the_quote_records_who_allowed_it()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var response = await owner.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 10000m, discount: 2000m));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Recorded rather than merely permitted. A large discount that nobody can be shown to have
        // approved is the one an auditor asks about.
        (await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId)).ApprovedByUserId.Should().NotBeNull();
    }

    [Fact]
    public async Task REQ_SALE_003_A_discount_larger_than_the_line_is_refused_outright()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var owner = await _fixture.ClientAsAsync(SalesFixture.OwnerEmail);

        var response = await owner.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 1000m, discount: 1500m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("DISCOUNT_EXCEEDS_LINE");
    }

    [Fact]
    public async Task REQ_SALE_003_A_line_with_no_quantity_is_refused()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        (await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(0, 1000m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task REQ_SALE_005_A_sent_quote_refuses_an_edit_and_offers_a_revision_instead()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 1, 5000m);
        (await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var edit = await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 1000m));

        // A figure somebody has already been shown must keep meaning what it meant (BR-SALE-05).
        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await edit.Content.ReadAsStringAsync();
        body.Should().Contain("QUOTE_NOT_EDITABLE");
        body.Should().Contain("Revise");
    }

    [Fact]
    public async Task REQ_SALE_005_Revising_copies_the_lines_into_a_new_number_and_withdraws_the_original()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 2, 4000m);
        await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { });

        var revised = await sales.PostAsync($"/api/v1/quotes/{quoteId}/revise", null);
        revised.StatusCode.Should().Be(HttpStatusCode.Created);

        var revision = (await revised.Content.ReadFromJsonAsync<CreatedQuote>())!;

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var original = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);
        var copy = await db.Quotes.AsNoTracking().Include(q => q.Lines).SingleAsync(q => q.Id == revision.Id);

        original.Status.Should().Be(QuoteStatus.Withdrawn);
        copy.Status.Should().Be(QuoteStatus.Draft);
        copy.RevisionOfQuoteId.Should().Be(quoteId);
        copy.QuoteNumber.Should().NotBe(original.QuoteNumber);

        // The figures come across, so a revision starts from what was quoted rather than from
        // nothing.
        copy.Lines.Should().ContainSingle();
        copy.GrandTotal.Should().Be(original.GrandTotal);
    }

    [Fact]
    public async Task REQ_SALE_005_A_draft_is_edited_directly_rather_than_revised()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsync($"/api/v1/quotes/{quoteId}/revise", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("QUOTE_STILL_DRAFT");
    }

    [Fact]
    public async Task REQ_SALE_002_A_quote_with_no_lines_cannot_be_sent()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("QUOTE_EMPTY");
    }

    [Fact]
    public async Task REQ_SALE_002_Sending_defaults_the_validity_to_fifteen_days_and_refuses_more_than_ninety()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 1, 1000m);

        var tooLong = await sales.PostAsJsonAsync(
            $"/api/v1/quotes/{quoteId}/send",
            new { validUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(120) });

        tooLong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("VALIDITY_TOO_LONG");

        (await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quote = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);

        quote.ValidUntil.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15));
        quote.IssuedOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task REQ_SALE_006_Accepting_twice_is_the_same_as_accepting_once()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 1, 7500m);
        await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { });

        (await sales.PostAsync($"/api/v1/quotes/{quoteId}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The customer who clicks twice, or the second salesperson recording the same call, must
        // not produce two of anything (BR-SALE-06).
        (await sales.PostAsync($"/api/v1/quotes/{quoteId}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId)).Status.Should().Be(QuoteStatus.Accepted);
    }

    [Fact]
    public async Task REQ_SALE_006_A_draft_cannot_be_accepted_because_nobody_has_seen_it()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsync($"/api/v1/quotes/{quoteId}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("QUOTE_NOT_SENT");
    }

    [Fact]
    public async Task REQ_SALE_006_Rejecting_records_the_reason_the_customer_gave()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 1, 3000m);
        await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/send", new { });

        (await sales.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/reject", new { reason = "Went with an in-house build." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quote = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);

        quote.Status.Should().Be(QuoteStatus.Rejected);
        quote.RejectReason.Should().Be("Went with an in-house build.");
    }

    [Fact]
    public async Task REQ_SALE_002_A_quote_past_its_validity_is_expired_by_the_sweep_and_a_draft_is_not()
    {
        var nonce = Nonce();
        var sentId = await DraftAsync(nonce);
        var draftId = await DraftAsync($"{nonce}-draft");
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, sentId, 1, 2000m);
        await sales.PostAsJsonAsync($"/api/v1/quotes/{sentId}/send", new { });

        using (var arrange = _fixture.NewScope())
        {
            var db = arrange.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Quotes.Where(q => q.Id == sentId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.ValidUntil, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        }

        using var scope = _fixture.NewScope();
        var quotes = scope.ServiceProvider.GetRequiredService<SoftwareManagement.Application.Sales.IQuoteService>();
        (await quotes.ExpireDueAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.Quotes.AsNoTracking().SingleAsync(q => q.Id == sentId)).Status.Should().Be(QuoteStatus.Expired);

        // A draft nobody finished is not an offer that lapsed.
        (await db2.Quotes.AsNoTracking().SingleAsync(q => q.Id == draftId)).Status.Should().Be(QuoteStatus.Draft);
    }

    [Fact]
    public async Task NFR_AUTHZ_02_The_editor_is_refused_the_whole_of_quoting()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var editor = await _fixture.ClientAsAsync(SalesFixture.EditorEmail);

        // The editor maintains the site. Money is not theirs to see or move (AZ-40..AZ-45).
        (await editor.GetAsync("/api/v1/quotes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync($"/api/v1/quotes/{quoteId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(1, 100m)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_SALE_001_The_quote_list_and_detail_read_back_what_was_written()
    {
        var nonce = Nonce();
        var quoteId = await DraftAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await AddLineAsync(sales, quoteId, 3, 1200m);

        var detail = await sales.GetFromJsonAsync<QuoteDetailRow>($"/api/v1/quotes/{quoteId}");

        detail!.Lines.Should().ContainSingle();
        detail.Lines[0].Quantity.Should().Be(3);
        detail.GrandTotal.Should().Be(4248.00m);
        detail.IsEditable.Should().BeTrue();

        var list = await sales.GetFromJsonAsync<List<QuoteSummaryRow>>("/api/v1/quotes?pageSize=100");
        list!.Should().Contain(q => q.Id == quoteId && q.LineCount == 1);
    }

    private static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> DraftAsync(string nonce)
    {
        var (organisationId, contactId) = await _fixture.AddCustomerAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId });
        await EnsureCreatedAsync(created);

        return (await created.Content.ReadFromJsonAsync<CreatedQuote>())!.Id;
    }

    private object Line(int quantity, decimal unitPrice, decimal discount = 0m) => new
    {
        productId = _fixture.ProductId,
        description = "One year of the thing, supported.",
        quantity,
        unitPrice,
        discountAmount = discount,
        taxRatePercent = 18.00m,
    };

    private async Task AddLineAsync(HttpClient client, Guid quoteId, int quantity, decimal unitPrice)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/quotes/{quoteId}/lines", Line(quantity, unitPrice));
        await EnsureCreatedAsync(response);
    }

    /// <summary>Fails with the server's own explanation rather than a bare status code.</summary>
    private static async Task EnsureCreatedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
    }

    private sealed record CreatedQuote(Guid Id, string QuoteNumber);

    private sealed record QuoteSummaryRow(Guid Id, string QuoteNumber, string Status, decimal GrandTotal, int LineCount);

    private sealed record QuoteLineRowDto(Guid Id, string Description, int Quantity, decimal UnitPrice, decimal LineTotal, decimal TaxAmount);

    private sealed record QuoteDetailRow(
        Guid Id,
        string QuoteNumber,
        string Status,
        decimal SubTotal,
        decimal TaxTotal,
        decimal GrandTotal,
        bool IsEditable,
        IReadOnlyList<QuoteLineRowDto> Lines);
}
