using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Infrastructure.Content;

/// <summary>
/// The settings table, read once and held.
///
/// It is a singleton with its own scope for loading, because the alternative is every request
/// paying for the same eight rows. The load is guarded by a lock so a burst of first requests reads
/// the table once rather than eight times; after that it is a dictionary lookup.
/// </summary>
public sealed class SystemSettingsCache(IServiceScopeFactory scopeFactory) : ISystemSettings
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly Lock _gate = new();

    private Dictionary<string, string?>? _values;

    public string? Value(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var values = _values;

        if (values is null)
        {
            lock (_gate)
            {
                _values ??= Load();
                values = _values;
            }
        }

        return values.TryGetValue(key, out var value) ? value : null;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _values = null;
        }
    }

    private Dictionary<string, string?> Load()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return dbContext.SystemSettings
            .AsNoTracking()
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
    }
}
