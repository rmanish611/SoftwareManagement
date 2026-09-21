using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Domain.Crm;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.IntegrationTests.Sales;

/// <summary>
/// Customer companies: the number that has to be right, the person who has to be reachable, and
/// the deletion that is not allowed to happen.
/// </summary>
[Collection(SalesTestGroup.Name)]
public sealed class OrganisationTests(SalesFixture fixture)
{
    private readonly SalesFixture _fixture = fixture;

    [Fact]
    public void REQ_CUST_001_The_GSTIN_rule_accepts_a_real_shape_and_refuses_the_ways_it_goes_wrong()
    {
        Gstin.IsValid("29ABCDE1234F1Z5").Should().BeTrue();

        // Fourteen characters, the commonest mistake: a digit dropped while copying.
        Gstin.IsValid("29ABCDE1234F1Z").Should().BeFalse();

        // The eleventh character is a literal Z and nothing else.
        Gstin.IsValid("29ABCDE1234F1Y5").Should().BeFalse();

        // Letters where the state code goes.
        Gstin.IsValid("AB ABCDE1234F1Z5".Replace(" ", string.Empty, StringComparison.Ordinal)).Should().BeFalse();

        Gstin.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void REQ_CUST_001_A_number_typed_in_lower_case_is_still_the_right_number()
    {
        Gstin.Normalise("  29abcde1234f1z5  ").Should().Be("29ABCDE1234F1Z5");
        Gstin.Normalise("   ").Should().BeNull();
    }

    [Fact]
    public async Task REQ_CUST_001_An_organisation_is_created_as_a_prospect_and_read_back_from_the_database()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"PROBE-{nonce} Private Limited",
            displayName = $"PROBE-{nonce}",
            gstin = "29ABCDE1234F1Z5",
            city = "Bengaluru",
            state = "Karnataka",
            country = "in",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(o => o.DisplayName == $"PROBE-{nonce}");

        // Prospect until somebody buys something. Calling every new row a customer makes the
        // pipeline report meaningless (REQ-CUST-001).
        organisation.Status.Should().Be(OrganisationStatus.Prospect);
        organisation.Gstin.Should().Be("29ABCDE1234F1Z5");
        organisation.Country.Should().Be("IN");
    }

    [Fact]
    public async Task REQ_CUST_001_A_fourteen_character_GSTIN_is_refused_and_the_refusal_carries_the_pattern()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var response = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"PROBE-{nonce}",
            gstin = "29ABCDE1234F1Z",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("GSTIN_INVALID");

        // The pattern travels with the refusal, so the form shows what was expected rather than
        // repeating the rule in a second place that can drift.
        body.Should().Contain("expectedPattern");
        body.Should().Contain("15 characters");
    }

    [Fact]
    public async Task REQ_CUST_002_The_same_GSTIN_twice_is_a_conflict_naming_the_company_that_holds_it()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        const string Number = "27AAACR5055K1Z5";

        var first = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"First {nonce}", gstin = Number });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"Second {nonce}", gstin = Number });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("GSTIN_DUPLICATE");
    }

    [Fact]
    public async Task REQ_CUST_003_The_first_contact_at_a_company_is_the_one_to_ring()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"PROBE-{nonce}" });
        var organisationId = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new
        {
            fullName = "First Person",
            email = $"first-{nonce}@probe.test",
        });

        await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new
        {
            fullName = "Second Person",
            email = $"second-{nonce}@probe.test",
        });

        var detail = await sales.GetFromJsonAsync<OrganisationDetailRow>($"/api/v1/organisations/{organisationId}");

        detail!.Contacts.Should().HaveCount(2);
        detail.Contacts.Count(c => c.IsPrimary).Should().Be(1);
        detail.Contacts.Single(c => c.IsPrimary).FullName.Should().Be("First Person");
    }

    [Fact]
    public async Task REQ_CUST_003_Naming_a_new_primary_contact_demotes_the_old_one()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"PROBE-{nonce}" });
        var organisationId = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new
        {
            fullName = "First Person",
            email = $"first-{nonce}@probe.test",
        });

        await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new
        {
            fullName = "New Boss",
            email = $"boss-{nonce}@probe.test",
            isPrimary = true,
        });

        var detail = await sales.GetFromJsonAsync<OrganisationDetailRow>($"/api/v1/organisations/{organisationId}");

        // Exactly one person to ring. Two primaries is the same as none.
        detail!.Contacts.Count(c => c.IsPrimary).Should().Be(1);
        detail.Contacts.Single(c => c.IsPrimary).FullName.Should().Be("New Boss");
    }

    [Fact]
    public async Task REQ_CUST_003_The_same_email_twice_at_one_company_is_refused()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"PROBE-{nonce}" });
        var organisationId = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        var email = $"same-{nonce}@probe.test";

        (await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new { email }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var again = await sales.PostAsJsonAsync($"/api/v1/organisations/{organisationId}/contacts", new { email });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("CONTACT_EXISTS");
    }

    [Fact]
    public async Task REQ_CUST_003_The_same_person_may_appear_at_two_companies_because_people_change_jobs()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);
        var email = $"mover-{nonce}@personal.test";

        foreach (var name in new[] { $"Old Employer {nonce}", $"New Employer {nonce}" })
        {
            var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = name });
            var id = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

            (await sales.PostAsJsonAsync($"/api/v1/organisations/{id}/contacts", new { email }))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Unique per organisation, not globally (BR-CUST-02).
        (await db.Contacts.AsNoTracking().CountAsync(c => c.Email == email)).Should().Be(2);
    }

    [Fact]
    public async Task REQ_CUST_006_A_company_is_retired_rather_than_removed()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"PROBE-{nonce}" });
        var organisationId = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        (await sales.PostAsync($"/api/v1/organisations/{organisationId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Still there, so a quote or an invoice pointing at it still points at something
        // (BR-CUST-03).
        (await db.Organisations.AsNoTracking().SingleAsync(o => o.Id == organisationId))
            .Status.Should().Be(OrganisationStatus.Inactive);
    }

    [Fact]
    public async Task REQ_CUST_004_A_company_whose_name_differs_only_by_its_suffix_is_offered_as_a_match()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var first = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"Acme {nonce} Technologies Private Limited",
            displayName = $"Acme {nonce} Technologies Private Limited",
        });

        var firstId = (await first.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        var second = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"ACME {nonce} technologies Pvt Ltd",
            displayName = $"ACME {nonce} technologies Pvt Ltd",
        });

        var secondId = (await second.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        var matches = await sales.GetFromJsonAsync<List<MatchRow>>($"/api/v1/organisations/{secondId}/duplicates");

        var match = matches!.Should().ContainSingle(m => m.Id == firstId).Subject;
        match.Reason.Should().Contain("company name");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Surfaced, never merged automatically (BR-CUST-04).
        (await db.Organisations.AsNoTracking().CountAsync(o => o.Id == firstId || o.Id == secondId)).Should().Be(2);
    }

    [Fact]
    public async Task REQ_CUST_004_Two_companies_sharing_an_email_domain_are_offered_as_a_match()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);
        var domain = $"shared-{nonce}.test";

        var ids = new List<Guid>();

        foreach (var (name, person) in new[] { ($"North {nonce}", "north"), ($"South {nonce}", "south") })
        {
            var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = name });
            var id = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;
            await sales.PostAsJsonAsync($"/api/v1/organisations/{id}/contacts", new { email = $"{person}@{domain}" });
            ids.Add(id);
        }

        var matches = await sales.GetFromJsonAsync<List<MatchRow>>($"/api/v1/organisations/{ids[1]}/duplicates");

        matches!.Should().Contain(m => m.Id == ids[0]);
        matches.Single(m => m.Id == ids[0]).Reason.Should().Contain(domain);
    }

    [Fact]
    public async Task REQ_CUST_004_A_company_with_nothing_in_common_is_offered_nothing()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"Entirely Distinct {nonce} Holdings",
        });

        var id = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;
        var matches = await sales.GetFromJsonAsync<List<MatchRow>>($"/api/v1/organisations/{id}/duplicates");

        // A suggestion list that is never empty is one nobody reads.
        matches!.Should().BeEmpty();
    }

    [Fact]
    public async Task REQ_CUST_007_The_list_can_be_searched_by_name_or_by_GSTIN()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"Searchable {nonce} Industries",
            gstin = "07AAGFF2194N1Z1",
        });

        var byName = await sales.GetFromJsonAsync<List<OrganisationSummaryRow>>($"/api/v1/organisations?search=Searchable {nonce}");
        byName!.Should().ContainSingle();

        var byGstin = await sales.GetFromJsonAsync<List<OrganisationSummaryRow>>("/api/v1/organisations?search=07AAGFF2194N1Z1");
        byGstin!.Should().ContainSingle();

        var byNothing = await sales.GetFromJsonAsync<List<OrganisationSummaryRow>>($"/api/v1/organisations?search=absent-{nonce}");
        byNothing!.Should().BeEmpty();
    }

    [Fact]
    public async Task NFR_AUTHZ_02_The_editor_is_refused_the_customer_records()
    {
        var editor = await _fixture.ClientAsAsync(SalesFixture.EditorEmail);

        // Personal data about other people's staff is not the site editor's to read (AZ-37, AZ-38).
        (await editor.GetAsync("/api/v1/organisations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await editor.PostAsJsonAsync("/api/v1/organisations", new { legalName = "Should never exist" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_CUST_002_A_GSTIN_freed_by_retiring_a_company_may_be_used_again()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        const string Number = "19AAACR5055K1Z2";

        var first = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"Old {nonce}", gstin = Number });
        var firstId = (await first.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        await sales.PostAsync($"/api/v1/organisations/{firstId}/deactivate", null);

        // Uniqueness is across *active* organisations. A business that was wound up and
        // re-registered under the same number is a real thing (BR-CUST-01).
        (await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"New {nonce}", gstin = Number }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task REQ_CUST_005_The_billing_address_is_stored_and_a_three_character_postal_code_is_refused()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var refused = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"PROBE-{nonce}",
            addressLine1 = "12 Industrial Estate",
            city = "Pune",
            state = "Maharashtra",
            postalCode = "411",
        });

        // An invoice with a three-character postal code is an invoice that does not arrive.
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<ProblemRow>())!.Field.Should().Be("postalCode");

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"PROBE-{nonce}",
            addressLine1 = "12 Industrial Estate",
            city = "Pune",
            state = "Maharashtra",
            postalCode = "411001",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;
        var detail = await sales.GetFromJsonAsync<OrganisationDetailRow>($"/api/v1/organisations/{id}");

        detail!.PostalCode.Should().Be("411001");
        detail.City.Should().Be("Pune");
    }

    [Fact]
    public async Task REQ_CUST_005_A_company_with_no_address_at_all_is_still_accepted()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        // The address is needed on an invoice, not on the day somebody first writes the company
        // down. Demanding it here would mean a salesperson on a call cannot record who rang.
        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"PROBE-{nonce}" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;
        var detail = await sales.GetFromJsonAsync<OrganisationDetailRow>($"/api/v1/organisations/{id}");

        detail!.PostalCode.Should().BeNull();
        detail.City.Should().BeNull();
    }

    [Fact]
    public async Task REQ_CUST_006_Deleting_a_company_referenced_by_a_quote_is_refused_and_the_quote_survives()
    {
        var nonce = Nonce();
        var (organisationId, contactId) = await _fixture.AddCustomerAsync(nonce);
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var quote = await sales.PostAsJsonAsync("/api/v1/quotes", new { organisationId, contactId });
        quote.StatusCode.Should().Be(HttpStatusCode.Created);

        var deleted = await sales.DeleteAsync($"/api/v1/organisations/{organisationId}");

        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await deleted.Content.ReadAsStringAsync();
        body.Should().Contain("ORGANISATION_IN_USE");
        body.Should().Contain("quoteCount");

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The quote still resolves to a company after the attempt, which is the whole point of
        // refusing (BR-CUST-03).
        (await db.Quotes.AsNoTracking().CountAsync(q => q.OrganisationId == organisationId)).Should().Be(1);
        (await db.Organisations.AsNoTracking().AnyAsync(o => o.Id == organisationId)).Should().BeTrue();
    }

    [Fact]
    public async Task REQ_CUST_007_A_page_size_of_five_hundred_is_clamped_to_a_hundred()
    {
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var rows = await sales.GetFromJsonAsync<List<OrganisationSummaryRow>>("/api/v1/organisations?pageSize=500");

        // Asked for five hundred, given at most a hundred. A caller cannot make the server read
        // the whole table by asking nicely (NFR-PERF-04).
        rows!.Count.Should().BeLessThanOrEqualTo(100);

        (await sales.GetFromJsonAsync<List<OrganisationSummaryRow>>("/api/v1/organisations?pageSize=0"))!
            .Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task REQ_CUST_008_The_customer_list_exports_as_a_CSV_with_the_columns_the_screen_shows()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        await sales.PostAsJsonAsync("/api/v1/organisations", new
        {
            legalName = $"Exported, Comma {nonce} Limited",
            displayName = $"Exported, Comma {nonce}",
            city = "Chennai",
        });

        var response = await sales.GetAsync("/api/v1/organisations/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        // Split on the CRLF the file actually uses. Splitting on LF alone leaves a carriage return
        // on the end of every field, which is how an export that is fine gets "fixed".
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("DisplayName,LegalName,Gstin,City,State,PostalCode,Country,Status,ContactCount");
        lines.Length.Should().BeGreaterThan(1);

        // A company name with a comma in it is ordinary. Quoting it is what stops the file
        // splitting into the wrong number of columns.
        csv.Should().Contain($"\"Exported, Comma {nonce}\"");
    }

    [Fact]
    public async Task REQ_CUST_008_An_editor_cannot_export_the_customer_list()
    {
        var editor = await _fixture.ClientAsAsync(SalesFixture.EditorEmail);

        (await editor.GetAsync("/api/v1/organisations/export")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_CUST_004_Converting_a_lead_onto_an_existing_company_creates_no_second_company()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);

        var created = await sales.PostAsJsonAsync("/api/v1/organisations", new { legalName = $"Existing {nonce} Limited" });
        var organisationId = (await created.Content.ReadFromJsonAsync<CreatedOrganisation>())!.Id;

        var leadId = await AddLeadAsync(nonce);

        var converted = await sales.PostAsJsonAsync($"/api/v1/leads/{leadId}/convert", new
        {
            organisationId,
            contactEmail = $"buyer-{nonce}@existing.test",
        });

        converted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The point of the requirement: no second company record for a customer already on file
        // (REQ-CUST-004).
        (await db.Organisations.AsNoTracking().CountAsync(o => o.DisplayName == $"Existing {nonce} Limited"))
            .Should().Be(1);

        var lead = await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);
        lead.OrganisationId.Should().Be(organisationId);
    }

    [Fact]
    public async Task REQ_CUST_004_A_conversion_naming_no_company_at_all_is_refused()
    {
        var nonce = Nonce();
        var sales = await _fixture.ClientAsAsync(SalesFixture.SalesEmail);
        var leadId = await AddLeadAsync(nonce, companyName: null);

        var response = await sales.PostAsJsonAsync($"/api/v1/leads/{leadId}/convert", new
        {
            contactEmail = $"nobody-{nonce}@example.test",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>A lead to convert, written straight to the database.</summary>
    private async Task<Guid> AddLeadAsync(string nonce, string? companyName = "Probe Industries")
    {
        using var scope = _fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<SoftwareManagement.Application.Leads.ISlaCalculator>();

        var now = DateTime.UtcNow;

        var lead = new SoftwareManagement.Domain.Leads.Lead
        {
            Id = Guid.NewGuid(),
            FullName = $"PROBE-{nonce}",
            Email = $"probe-{nonce}@example.test",
            CompanyName = companyName,
            Stage = SoftwareManagement.Domain.Leads.LeadStage.Qualified,
            CreatedAtUtc = now,
            SlaDueAtUtc = sla.FirstResponseDueUtc(now),
            CreatedBy = "test-fixture",
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    private sealed record ProblemRow(string? Title, string? Detail, int? Status, string? Code, string? Field);

    private static string Nonce() => Guid.NewGuid().ToString("N")[..8];

    private sealed record CreatedOrganisation(Guid Id, string LegalName, string DisplayName);

    private sealed record ContactRowDto(Guid Id, string FullName, string Email, bool IsPrimary);

    private sealed record OrganisationDetailRow(
        Guid Id,
        string LegalName,
        string DisplayName,
        string? Gstin,
        string? City,
        string? State,
        string? PostalCode,
        string Status,
        IReadOnlyList<ContactRowDto> Contacts);

    private sealed record OrganisationSummaryRow(Guid Id, string DisplayName, string? Gstin, string Status, int ContactCount);

    private sealed record MatchRow(Guid Id, string DisplayName, string? Gstin, string Status, string Reason);
}
