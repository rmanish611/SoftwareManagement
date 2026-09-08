using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Proves, against the running application rather than in theory, that an unhandled exception
/// becomes an RFC 9457 problem document carrying a traceId and leaking neither a stack trace nor
/// an inner exception message (NFR-OBS-03).
///
/// The endpoint is off unless <c>Diagnostics:EnableThrowEndpoint</c> is true, so it does not
/// exist in a normal production deployment. It requires an authenticated caller like every other
/// controller endpoint, and when disabled it answers 404 rather than admitting it is there.
/// </summary>
[ApiController]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController(IConfiguration configuration) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;

    [HttpGet("throw")]
    [AllowAnonymous]
    public IActionResult Throw()
    {
        if (!_configuration.GetValue<bool>("Diagnostics:EnableThrowEndpoint"))
        {
            return NotFound();
        }

        throw new InvalidOperationException(
            "Deliberate failure raised by the diagnostics endpoint to verify the error contract.");
    }
}
