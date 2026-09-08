using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SoftwareManagement.Api.Infrastructure;
using SoftwareManagement.Infrastructure;
using SoftwareManagement.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to rolling files with a correlation id on every line (NFR-OBS-01, NFR-OBS-02).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// RFC 9457 problem details, with a traceId and never a stack trace (NFR-OBS-03).
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    });

var jwt = builder.Configuration.GetSection("Jwt");
var signingKey = jwt["Key"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it with `dotnet user-secrets` in development or the Jwt__Key " +
        "environment variable in production. See docs/ENVIRONMENT.md.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Permission policies are built on demand from the policy name, so all 70 permissions in the
// catalogue are usable as `[Authorize(Policy = ...)]` without registering each one by hand.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("process is running"), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// Migrate and seed on start-up only where it is safe to do so. In production the schema is applied
// by a reviewed idempotent script before deployment (NFR-DEP-02), so the application never migrates
// itself there; it only ensures the roles, permissions and first Owner exist.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    {
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    if (app.Configuration.GetValue("Database:SeedOnStartup", true) && await db.Database.CanConnectAsync().ConfigureAwait(false))
    {
        await seeder.SeedAsync().ConfigureAwait(false);
    }
}

app.UseSerilogRequestLogging();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

// Deny by default: every controller endpoint requires an authenticated caller unless it opts out
// with [AllowAnonymous] (BR-IAM-05, NFR-AUTHZ-01). The policy is attached to the mapped endpoints
// rather than set as a global fallback, because a global fallback also answers requests that match
// no endpoint at all, turning every unknown path into a 401 and hiding genuine routing mistakes
// behind an authentication error.
app.MapControllers().RequireAuthorization();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Exposed so the integration tests can host the real pipeline with WebApplicationFactory.</summary>
public partial class Program;
