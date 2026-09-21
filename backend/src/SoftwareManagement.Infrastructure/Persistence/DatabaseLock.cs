using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// A SQL Server application lock, held for the life of its own connection.
///
/// Every scheduled job takes one before it does anything, so a second application instance - if one
/// is ever started - cannot send the same dunning email twice (NFR-DEP-05). It is a named resource
/// rather than a row, because a row would need its own release semantics and a crashed instance
/// would leave it set; a session-owned application lock is released when the connection closes,
/// whatever killed the process.
///
/// The seeding lock was the first user of this and is now one of two (ASM-9).
/// </summary>
public sealed partial class DatabaseLock(AppDbContext dbContext, ILogger logger) : IAsyncDisposable
{
    public const string Seeding = "SoftwareManagement.DatabaseSeeder";

    public const string BillingSweep = "SoftwareManagement.BillingSweep";

    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILogger _logger = logger;
    private SqlConnection? _connection;
    private string? _resource;

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the named lock. Returns false when it could not
    /// be taken, which means another instance holds it and this one has nothing to do.
    /// </summary>
    public async Task<bool> AcquireAsync(string resource, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        _resource = resource;

        // A dedicated connection: the lock lives as long as it does, independent of any transaction
        // the work opens and closes along the way.
        _connection = new SqlConnection(_dbContext.Database.GetConnectionString());
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = _connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sp_getapplock";
        command.CommandTimeout = (int)timeout.TotalSeconds + 10;

        command.Parameters.AddWithValue("@Resource", resource);
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

        // Releasing is best-effort: closing the connection releases a session-owned lock anyway.
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "sp_releaseapplock";
            command.Parameters.AddWithValue("@Resource", _resource);
            command.Parameters.AddWithValue("@LockOwner", "Session");
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            // Logged rather than swallowed: a release that keeps failing is worth seeing.
            LogReleaseFailed(_logger, _resource ?? "unknown", ex.Message);
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Releasing the {resource} lock failed: {reason}. It is released when the connection closes.")]
    private static partial void LogReleaseFailed(ILogger logger, string resource, string reason);
}
