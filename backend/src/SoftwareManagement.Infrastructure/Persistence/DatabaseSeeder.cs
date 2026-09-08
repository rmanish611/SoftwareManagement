using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Settings;

namespace SoftwareManagement.Infrastructure.Persistence;

/// <summary>
/// Brings a fresh database to a usable state: the four roles, the whole permission catalogue, the
/// role-to-permission mapping and the first Owner account.
///
/// The Owner's password is never in a migration or in source. It comes from configuration
/// (`Seed:OwnerPassword`, supplied by user-secrets in development and by an environment variable on
/// a server); when it is absent the account is created without a password and the operator must run
/// a reset, which is the safe default for a machine nobody has configured yet.
/// </summary>
public sealed partial class DatabaseSeeder(
    AppDbContext dbContext,
    UserManager<AdminUser> userManager,
    RoleManager<AdminRole> roleManager,
    IConfiguration configuration,
    ILogger<DatabaseSeeder> logger)
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly UserManager<AdminUser> _userManager = userManager;
    private readonly RoleManager<AdminRole> _roleManager = roleManager;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<DatabaseSeeder> _logger = logger;

    private static readonly Dictionary<string, string> RoleDescriptions = new(StringComparer.Ordinal)
    {
        [RoleNames.Owner] = "The company owner. Everything, including users, settings, money and privacy operations.",
        [RoleNames.Sales] = "Leads, customers, quotes, tenants and invoices. No site content, settings or users.",
        [RoleNames.Editor] = "Site content, catalogue, projects and media. No personal data and no money.",
        [RoleNames.Auditor] = "Read-only across money and compliance, including the audit trail. Writes nothing.",
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Two instances starting together must not both insert the same role or the same owner.
        await using var seedLock = new SeedLock(_dbContext, _logger);
        if (!await seedLock.AcquireAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false))
        {
            LogSeedLockNotTaken(_logger);
            return;
        }

        await SeedRolesAsync().ConfigureAwait(false);
        await SeedPermissionsAsync(cancellationToken).ConfigureAwait(false);
        await SeedRolePermissionsAsync(cancellationToken).ConfigureAwait(false);
        await SeedSettingsAsync(cancellationToken).ConfigureAwait(false);
        await SeedProductCategoriesAsync(cancellationToken).ConfigureAwait(false);
        await SeedOwnerAsync().ConfigureAwait(false);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var roleName in RoleNames.All)
        {
            if (await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
            {
                continue;
            }

            await _roleManager.CreateAsync(new AdminRole(roleName)
            {
                Id = Guid.NewGuid(),
                Description = RoleDescriptions[roleName],
            }).ConfigureAwait(false);
        }
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Permissions
            .Select(p => p.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var missing = Permissions.Catalogue
            .Where(entry => !existing.Contains(entry.Name, StringComparer.Ordinal))
            .Select(entry => new Permission
            {
                Id = Guid.NewGuid(),
                Name = entry.Name,
                Category = entry.Category,
                Description = entry.Description,
                CreatedBy = "seed",
            })
            .ToList();

        if (missing.Count > 0)
        {
            _dbContext.Permissions.AddRange(missing);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogSeededPermissions(_logger, missing.Count);
        }
    }

    private async Task SeedRolePermissionsAsync(CancellationToken cancellationToken)
    {
        var permissionsByName = await _dbContext.Permissions
            .ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        var existing = await _dbContext.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var existingPairs = existing.Select(e => (e.RoleId, e.PermissionId)).ToHashSet();
        var added = 0;

        foreach (var roleName in RoleNames.All)
        {
            var role = await _roleManager.FindByNameAsync(roleName).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }

            foreach (var permissionName in RolePermissionMap.For(roleName))
            {
                if (!permissionsByName.TryGetValue(permissionName, out var permissionId))
                {
                    continue;
                }

                if (existingPairs.Contains((role.Id, permissionId)))
                {
                    continue;
                }

                _dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
                added++;
            }
        }

        if (added > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogSeededRolePermissions(_logger, added);
        }
    }

    private async Task SeedSettingsAsync(CancellationToken cancellationToken)
    {
        (string Key, string? Value, SettingValueType Type, string Category)[] defaults =
        [
            ("company.name", "Software Management", SettingValueType.Text, "Company"),
            ("company.city", "Noida, India", SettingValueType.Text, "Company"),
            ("company.displayTimeZone", "India Standard Time", SettingValueType.Text, "Company"),
            ("company.currency", "INR", SettingValueType.Text, "Company"),
            ("sla.businessHoursStart", "09:00", SettingValueType.Text, "Service level"),
            ("sla.businessHoursEnd", "18:00", SettingValueType.Text, "Service level"),
            ("sla.workingDays", "Mon,Tue,Wed,Thu,Fri,Sat", SettingValueType.Text, "Service level"),
            ("sla.firstResponseHours", "9", SettingValueType.Number, "Service level"),
        ];

        var existing = await _dbContext.SystemSettings
            .Select(s => s.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var missing = defaults
            .Where(d => !existing.Contains(d.Key, StringComparer.Ordinal))
            .Select(d => new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = d.Key,
                Value = d.Value,
                ValueType = d.Type,
                Category = d.Category,
                CreatedBy = "seed",
            })
            .ToList();

        if (missing.Count > 0)
        {
            _dbContext.SystemSettings.AddRange(missing);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The industries the company sells into. These are the buyer's starting point: someone runs a
    /// hospital, not "a product", so the catalogue is grouped by the job rather than by our
    /// internal naming (S-10). Categories are seeded because the first product cannot be created
    /// without one, and an empty category list would make a fresh install look broken.
    /// </summary>
    private async Task SeedProductCategoriesAsync(CancellationToken cancellationToken)
    {
        (string Slug, string Name, string Description, string IconKey, int SortOrder)[] defaults =
        [
            ("erp", "Enterprise resource planning", "Finance, inventory, purchase and reporting for a whole business in one system.", "erp", 1),
            ("healthcare", "Hospital and clinic", "Patient records, appointments, pharmacy, laboratory and billing for care providers.", "healthcare", 2),
            ("billing", "Billing and invoicing", "GST invoicing, subscriptions, receipts and collections for service businesses.", "billing", 3),
            ("education", "School and college", "Admissions, attendance, examinations, fees and results for education institutions.", "education", 4),
        ];

        var existing = await _dbContext.ProductCategories
            .Select(c => c.Slug)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var missing = defaults
            .Where(d => !existing.Contains(d.Slug, StringComparer.Ordinal))
            .Select(d => new ProductCategory
            {
                Id = Guid.NewGuid(),
                Slug = d.Slug,
                Name = d.Name,
                Description = d.Description,
                IconKey = d.IconKey,
                SortOrder = d.SortOrder,
                IsPublished = true,
                CreatedBy = "seed",
            })
            .ToList();

        if (missing.Count > 0)
        {
            _dbContext.ProductCategories.AddRange(missing);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogSeededProductCategories(_logger, missing.Count);
        }
    }

    private async Task SeedOwnerAsync()
    {
        var email = _configuration["Seed:OwnerEmail"] ?? "owner@softwaremanagement.test";
        if (await _userManager.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return;
        }

        var owner = new AdminUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = _configuration["Seed:OwnerName"] ?? "Owner",
            IsActive = true,
            CreatedBy = "seed",
        };

        var password = _configuration["Seed:OwnerPassword"];

        var result = string.IsNullOrWhiteSpace(password)
            ? await _userManager.CreateAsync(owner).ConfigureAwait(false)
            : await _userManager.CreateAsync(owner, password).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            LogOwnerSeedFailed(_logger, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await _userManager.AddToRoleAsync(owner, RoleNames.Owner).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(password))
        {
            LogOwnerWithoutPassword(_logger, email);
        }
    }

    // Source-generated logging: the arguments are only formatted when the level is enabled.
    [LoggerMessage(Level = LogLevel.Information, Message = "Another instance is already seeding; this one skipped it.")]
    private static partial void LogSeedLockNotTaken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {count} permissions.")]
    private static partial void LogSeededPermissions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {count} product categories.")]
    private static partial void LogSeededProductCategories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {count} role-permission assignments.")]
    private static partial void LogSeededRolePermissions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not seed the owner account: {errors}")]
    private static partial void LogOwnerSeedFailed(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Owner {email} was created without a password because Seed:OwnerPassword is not configured. Set it in user-secrets or run a password reset before signing in.")]
    private static partial void LogOwnerWithoutPassword(ILogger logger, string email);
}
