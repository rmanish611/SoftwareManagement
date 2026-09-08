using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Infrastructure.Persistence;
using SoftwareManagement.Infrastructure.Security;
using SoftwareManagement.Infrastructure.Time;

namespace SoftwareManagement.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the application needs to reach the outside world. The connection string
    /// is required: a missing one fails at startup with a named error rather than at the first
    /// request with a null reference (NFR-DEP-01).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connection = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set it with `dotnet user-secrets` in development " +
                "or the ConnectionStrings__Default environment variable in production. See docs/ENVIRONMENT.md.");
        }

        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connection, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            }));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SoftwareManagement.Infrastructure.Security.LockoutOptions>(
            configuration.GetSection(SoftwareManagement.Infrastructure.Security.LockoutOptions.SectionName));

        services
            .AddIdentityCore<AdminUser>(options =>
            {
                // BR-IAM-01: at least 12 characters using three of the four character classes.
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                // Lockout is enforced by AuthService against the LoginAttempts table, which is also
                // the Auditor's record, so Identity's own counter is not the source of truth.
                options.Lockout.AllowedForNewUsers = false;

                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddRoles<AdminRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<TokenFactory>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
