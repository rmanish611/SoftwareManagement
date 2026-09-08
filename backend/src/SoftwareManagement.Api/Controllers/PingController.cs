using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The walking skeleton's proof that the pipeline is wired: one anonymous route and one that
/// requires a token. The exit gate calls the secure route without a token and requires 401,
/// which is how deny-by-default is proven before any feature exists (NFR-AUTHZ-01).
/// </summary>
[ApiController]
[Route("api/v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet("public")]
    [AllowAnonymous]
    public ActionResult<PingResponse> Public() =>
        Ok(new PingResponse("public", DateTime.UtcNow));

    [HttpGet("secure")]
    public ActionResult<PingResponse> Secure() =>
        Ok(new PingResponse("secure", DateTime.UtcNow));
}

/// <param name="Scope">Which route answered.</param>
/// <param name="AtUtc">Server time in UTC, never local time (A-08).</param>
public sealed record PingResponse(string Scope, DateTime AtUtc);
