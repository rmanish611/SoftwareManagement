using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Sales;
using SoftwareManagement.Domain.Sales;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Sales;

/// <summary>
/// Hands out the next document number, without gaps and without duplicates.
///
/// The row is read and incremented in a single statement under `UPDLOCK, ROWLOCK`, so a second
/// request asking at the same moment waits rather than reading the same value. `MAX(number) + 1`
/// would let both read the same maximum and write the same number, and two quotes carrying
/// `Q/2026-27/00007` is worse than any gap.
///
/// The caller's transaction owns the allocation: if the quote fails to save, the number goes back
/// with it, which is what "gapless" requires (BR-SALE-01, BR-SALE-10).
/// </summary>
public sealed class NumberAllocator(AppDbContext dbContext) : INumberAllocator
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<string> NextAsync(string key, string prefix, DateOnly issuedOn, CancellationToken cancellationToken)
    {
        var financialYear = NumberSequence.FinancialYearOf(issuedOn);

        // OUTPUT returns the value this statement claimed, which is the only value it can be: the
        // row lock is held from the read to the write inside one statement.
        var claimed = await _dbContext.Database
            .SqlQuery<int>($"""
                UPDATE NumberSequences WITH (UPDLOCK, ROWLOCK)
                SET NextValue = NextValue + 1
                OUTPUT deleted.NextValue AS Value
                WHERE [Key] = {key} AND FinancialYear = {financialYear}
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (claimed.Count == 1)
        {
            return NumberSequence.Format(prefix, financialYear, claimed[0]);
        }

        // First document of a financial year. The unique index on (Key, FinancialYear) is what
        // makes this safe under a race: two requests both find no row, both insert, and one of
        // them loses and retries into the branch above.
        try
        {
            _dbContext.NumberSequences.Add(new NumberSequence
            {
                Id = Guid.NewGuid(),
                Key = key,
                FinancialYear = financialYear,
                NextValue = 2,
                CreatedBy = "system",
            });

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return NumberSequence.Format(prefix, financialYear, 1);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();

            var retried = await _dbContext.Database
                .SqlQuery<int>($"""
                    UPDATE NumberSequences WITH (UPDLOCK, ROWLOCK)
                    SET NextValue = NextValue + 1
                    OUTPUT deleted.NextValue AS Value
                    WHERE [Key] = {key} AND FinancialYear = {financialYear}
                    """)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (retried.Count != 1)
            {
                throw;
            }

            return NumberSequence.Format(prefix, financialYear, retried[0]);
        }
    }
}
