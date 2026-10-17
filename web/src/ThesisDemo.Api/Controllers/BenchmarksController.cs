using Microsoft.AspNetCore.Mvc;
using ThesisDemo.Infrastructure.Research;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/benchmarks")]
public sealed class BenchmarksController(BenchmarkStore benchmarks) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(benchmarks.List());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(8 * 1024 * 1024 + 4096)]
    public async Task<IActionResult> Import([FromForm] string name, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length > 8 * 1024 * 1024) return BadRequest("Invalid benchmark file.");
        try
        {
            await using var source = file.OpenReadStream();
            return Created("/api/v1/benchmarks", await benchmarks.ImportAsync(name, source, ct));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        { return BadRequest(new { code = "INVALID_BENCHMARK", message = ex.Message }); }
    }

    [HttpPost("{id:guid}/sanitize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sanitize(Guid id, CancellationToken ct)
    {
        try { return Created("/api/v1/benchmarks", await benchmarks.SanitizeAsync(id, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    public sealed record SplitRequest(double TrainRatio = 0.7, int Seed = 42);

    [HttpPost("{id:guid}/split")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Split(Guid id, [FromBody] SplitRequest request, CancellationToken ct)
    {
        try { return Ok(await benchmarks.SplitAsync(id, request.TrainRatio, request.Seed, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        { return BadRequest(new { code = "INVALID_SPLIT", message = ex.Message }); }
    }
}
