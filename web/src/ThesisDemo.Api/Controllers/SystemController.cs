using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ready", traceId = HttpContext.TraceIdentifier });

    [HttpPost("validate")]
    [ValidateAntiForgeryToken]
    public IActionResult Validate([FromBody] ValidationProbe request) => Ok(request);
}

public sealed class ValidationProbe
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = "";
}
