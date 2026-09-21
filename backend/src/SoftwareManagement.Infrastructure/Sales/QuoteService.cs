using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Sales;

/// <summary>
/// Quoting.
///
/// Two rules shape everything here. Money is rounded per line and the quote total is the sum of
/// those rounded lines, never a rounded sum (BR-SALE-03) - a customer who adds up the printed
/// lines must get the printed total. And a quote stops being editable the moment it is sent: after
/// that a change is a revision with its own number, so a figure somebody has been shown never
/// quietly becomes a different figure (BR-SALE-05).
/// </summary>
public sealed class QuoteService(
    AppDbContext dbContext,
    INumberAllocator numbers,
    IClock clock) : IQuoteService
{
    /// <summary>Fifteen days from issue, and never more than ninety (BR-SALE-02).</summary>
    public const int DefaultValidityDays = 15;

    public const int MaximumValidityDays = 90;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly INumberAllocator _numbers = numbers;
    private readonly IClock _clock = clock;

    public async Task<QuoteResult> CreateAsync(NewQuote quote, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var contact = await _dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == quote.ContactId, cancellationToken).ConfigureAwait(false);

        if (contact is null || contact.OrganisationId != quote.OrganisationId)
        {
            // A quote addressed to somebody at a different company is not a typo worth saving.
            return QuoteResult.Refused(
                "CONTACT_NOT_AT_ORGANISATION", "contactId", "That person is not at that organisation.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);

        // The number is allocated inside the same transaction that saves the quote, so a failure
        // to save takes the number back with it (BR-SALE-01).
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var created = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var number = await _numbers
                .NextAsync(NumberSequence.Quote, "Q", today, cancellationToken).ConfigureAwait(false);

            var row = new Quote
            {
                Id = Guid.NewGuid(),
                QuoteNumber = number,
                OrganisationId = quote.OrganisationId,
                ContactId = quote.ContactId,
                LeadId = quote.LeadId,
                Status = QuoteStatus.Draft,
                Currency = "INR",
                Notes = quote.Notes,
                CreatedBy = actor,
            };

            _dbContext.Quotes.Add(row);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return row;
        }).ConfigureAwait(false);

        return QuoteResult.Done(created.Id, created.QuoteNumber);
    }

    public async Task<QuoteResult> AddLineAsync(
        Guid quoteId, NewQuoteLine line, SalesActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(actor);

        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        if (!quote.IsEditable())
        {
            return QuoteResult.Conflict(
                "QUOTE_NOT_EDITABLE",
                $"This quote has been {quote.Status.ToString().ToLowerInvariant()}. Revise it to change the figures.",
                quote.Id);
        }

        if (line.Quantity <= 0)
        {
            return QuoteResult.Refused("QUANTITY_INVALID", "quantity", "A line needs at least one of something.");
        }

        if (line.UnitPrice < 0)
        {
            return QuoteResult.Refused("PRICE_INVALID", "unitPrice", "A price cannot be negative.");
        }

        var row = new QuoteLineItem
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            ProductId = line.ProductId,
            PricingPlanId = line.PricingPlanId,
            Description = line.Description.Trim(),
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            DiscountAmount = line.DiscountAmount,
            TaxRatePercent = line.TaxRatePercent,
            SortOrder = quote.Lines.Count + 1,
            CreatedBy = actor.Email,
        };

        if (row.DiscountAmount > row.Subtotal())
        {
            return QuoteResult.Refused(
                "DISCOUNT_EXCEEDS_LINE", "discountAmount", "A discount cannot be larger than the line it discounts.");
        }

        if (row.NeedsApproval() && !actor.CanApproveDiscount)
        {
            // About who is asking rather than about what they sent, which is why the controller
            // turns this into a 403 and not a 422 (BR-SALE-04).
            return QuoteResult.NeedsApproval(
                $"A discount above {QuoteLineItem.DiscountApprovalThreshold:P0} of the line needs the owner's approval.");
        }

        if (row.NeedsApproval())
        {
            // Recorded, not merely permitted: the quote names who allowed the exception.
            quote.ApprovedByUserId = actor.UserId;
        }

        row.Recalculate();

        // Added to the set, and to the set only.
        //
        // Two ways of getting this wrong were tried first and both are worth recording. Adding it
        // to `quote.Lines` as well puts it in that collection twice, because the change tracker's
        // fixup already put it there the moment the line's QuoteId matched a tracked quote - and
        // the totals below then count it twice. Adding it *only* to the navigation leaves EF
        // marking it Modified rather than Added, its rule for an entity discovered through a
        // navigation with a key already set, and the UPDATE for a row that does not exist comes
        // back as a concurrency failure.
        _dbContext.QuoteLineItems.Add(row);

        Total(quote, quote.Lines);
        quote.ModifiedBy = actor.Email;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return QuoteResult.Done(quote.Id, quote.QuoteNumber);
    }

    public async Task<QuoteResult> RemoveLineAsync(Guid quoteId, Guid lineId, string actor, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        if (!quote.IsEditable())
        {
            return QuoteResult.Conflict(
                "QUOTE_NOT_EDITABLE", "A sent quote cannot be changed. Revise it instead.", quote.Id);
        }

        var line = quote.Lines.FirstOrDefault(l => l.Id == lineId);

        if (line is null)
        {
            return QuoteResult.NotFound();
        }

        _dbContext.QuoteLineItems.Remove(line);
        quote.Lines.Remove(line);

        Total(quote, quote.Lines);
        quote.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return QuoteResult.Done(quote.Id, quote.QuoteNumber);
    }

    public async Task<QuoteResult> SendAsync(Guid quoteId, DateOnly? validUntil, string actor, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        if (quote.Status != QuoteStatus.Draft)
        {
            return QuoteResult.Conflict("QUOTE_ALREADY_SENT", "This quote has already left the building.", quote.Id);
        }

        if (quote.Lines.Count == 0)
        {
            return QuoteResult.Refused("QUOTE_EMPTY", "lines", "A quote with no lines is not a quote.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var until = validUntil ?? today.AddDays(DefaultValidityDays);

        if (until <= today)
        {
            return QuoteResult.Refused("VALIDITY_PAST", "validUntil", "A quote cannot expire before it is sent.");
        }

        if (until > today.AddDays(MaximumValidityDays))
        {
            return QuoteResult.Refused(
                "VALIDITY_TOO_LONG",
                "validUntil",
                $"A quote may be held open for at most {MaximumValidityDays} days.");
        }

        quote.Status = QuoteStatus.Sent;
        quote.IssuedOn = today;
        quote.ValidUntil = until;
        quote.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return QuoteResult.Done(quote.Id, quote.QuoteNumber);
    }

    public async Task<QuoteResult> ReviseAsync(Guid quoteId, string actor, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        if (quote.Status == QuoteStatus.Draft)
        {
            return QuoteResult.Conflict("QUOTE_STILL_DRAFT", "This quote is still a draft. Edit it directly.", quote.Id);
        }

        if (quote.Status == QuoteStatus.Accepted)
        {
            return QuoteResult.Conflict("QUOTE_ACCEPTED", "An accepted quote is not revised; raise a new one.", quote.Id);
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var revision = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var number = await _numbers
                .NextAsync(NumberSequence.Quote, "Q", today, cancellationToken).ConfigureAwait(false);

            var copy = new Quote
            {
                Id = Guid.NewGuid(),
                QuoteNumber = number,
                OrganisationId = quote.OrganisationId,
                ContactId = quote.ContactId,
                LeadId = quote.LeadId,
                Status = QuoteStatus.Draft,
                Currency = quote.Currency,
                Notes = quote.Notes,
                RevisionOfQuoteId = quote.Id,
                CreatedBy = actor,
            };

            foreach (var line in quote.Lines.OrderBy(l => l.SortOrder))
            {
                var copied = new QuoteLineItem
                {
                    Id = Guid.NewGuid(),
                    QuoteId = copy.Id,
                    ProductId = line.ProductId,
                    PricingPlanId = line.PricingPlanId,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountAmount = line.DiscountAmount,
                    TaxRatePercent = line.TaxRatePercent,
                    SortOrder = line.SortOrder,
                    CreatedBy = actor,
                };

                copied.Recalculate();
                copy.Lines.Add(copied);
            }

            Total(copy, copy.Lines);

            // The original is withdrawn rather than deleted or edited. Its number keeps meaning
            // what it meant, and the revision says what it replaces.
            quote.Status = QuoteStatus.Withdrawn;
            quote.ModifiedBy = actor;

            _dbContext.Quotes.Add(copy);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return copy;
        }).ConfigureAwait(false);

        return QuoteResult.Done(revision.Id, revision.QuoteNumber);
    }

    public async Task<QuoteResult> AcceptAsync(Guid quoteId, string actor, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        // Idempotent on purpose: the customer who clicks twice, or the salesperson who records an
        // acceptance somebody else already recorded, must not produce two of anything (BR-SALE-06).
        if (quote.Status == QuoteStatus.Accepted)
        {
            return QuoteResult.Done(quote.Id, quote.QuoteNumber);
        }

        if (quote.Status != QuoteStatus.Sent)
        {
            return QuoteResult.Conflict(
                "QUOTE_NOT_SENT", $"A {quote.Status.ToString().ToLowerInvariant()} quote cannot be accepted.", quote.Id);
        }

        quote.Status = QuoteStatus.Accepted;
        quote.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return QuoteResult.Done(quote.Id, quote.QuoteNumber);
    }

    public async Task<QuoteResult> RejectAsync(Guid quoteId, string reason, string actor, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(quoteId, cancellationToken).ConfigureAwait(false);

        if (quote is null)
        {
            return QuoteResult.NotFound();
        }

        if (quote.Status != QuoteStatus.Sent)
        {
            return QuoteResult.Conflict(
                "QUOTE_NOT_SENT", $"A {quote.Status.ToString().ToLowerInvariant()} quote cannot be rejected.", quote.Id);
        }

        quote.Status = QuoteStatus.Rejected;
        quote.RejectReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        quote.ModifiedBy = actor;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return QuoteResult.Done(quote.Id, quote.QuoteNumber);
    }

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        // Only a sent quote expires. A draft nobody finished is not an offer that lapsed, and an
        // accepted one is a deal (BR-SALE-02).
        return await _dbContext.Quotes
            .Where(q => q.Status == QuoteStatus.Sent && q.ValidUntil != null && q.ValidUntil < today)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.Status, QuoteStatus.Expired).SetProperty(q => q.ModifiedBy, "sweep"),
                cancellationToken).ConfigureAwait(false);
    }

    private Task<Quote?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    /// <summary>
    /// The quote total is the sum of the rounded lines. Never a rounded sum: a customer who adds
    /// up the printed lines has to get the printed total (BR-SALE-03).
    /// </summary>
    private static void Total(Quote quote, IEnumerable<QuoteLineItem> lines)
    {
        var all = lines.ToList();

        quote.SubTotal = all.Sum(l => l.Subtotal());
        quote.DiscountTotal = all.Sum(l => QuoteLineItem.Round(l.DiscountAmount));
        quote.TaxTotal = all.Sum(l => l.TaxAmount);
        quote.GrandTotal = all.Sum(l => l.LineTotal + l.TaxAmount);
    }
}
