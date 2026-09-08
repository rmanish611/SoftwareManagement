using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// A SQL Server application lock, so that two application instances starting at the same moment
/// cannot both seed the roles and both insert "Owner".
///
/// The problem is real in production the first time the site is scaled to two instances, and it is
/// real today in the test suite, where several hosts share one database. A check-then-insert is a
/// race whatever the caller does, so the fix belongs here rather than in a retry around the symptom
/// (NFR-DEP-05: every scheduled or start-up job takes a database lock).
/// </summary>
public sealed partial class SeedLock(AppDbContext dbContext, ILogger logger) : IAsyncDisposable
{
    private const string ResourceName = "SoftwareManagement.DatabaseSeeder";

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILogger _logger = logger;
    private SqlConnection? _connection;

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the lock. Returns false when it could not be
    /// taken, which means another instance is seeding and this one has nothing to do.
    /// </summary>
    public async Task<bool> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // A dedicated connection: the lock is held for its lifetime, independent of any transaction
        // the seeding work opens and closes along the way.
        _connection = new SqlConnection(_dbContext.Database.GetConnectionString());
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = _connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sp_getapplock";
        command.CommandTimeout = (int)timeout.TotalSeconds + 10;

        command.Parameters.AddWithValue("@Resource", ResourceName);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Session");
        command.Parameters.AddWithValue("@LockTimeout", (int)timeout.TotalMilliseconds);

        var result = command.Parameters.Add("@Result", SqlDbType.Int);
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // 0 = granted immediately, 1 = granted after waiting. Negative values mean timeout,
        // deadlock, cancellation or a parameter error.
        var code = (int)result.Value;
        if (code >= 0)
        {
            return true;
        }

        await DisposeAsync().ConfigureAwait(false);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        // Releasing is best-effort: closing the connection releases a Session-owned lock anyway.
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "sp_releaseapplock";
            command.Parameters.AddWithValue("@Resource", ResourceName);
            command.Parameters.AddWithValue("@LockOwner", "Session");
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            // Closing the connection releases a Session-owned lock anyway, so this is recoverable.
            // It is logged rather than swallowed: a release that keeps failing is worth seeing.
            LogReleaseFailed(_logger, ex.Message);
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Releasing the seed lock failed: {reason}. The lock is released when the connection closes.")]
    private static partial void LogReleaseFailed(ILogger logger, string reason);
}
