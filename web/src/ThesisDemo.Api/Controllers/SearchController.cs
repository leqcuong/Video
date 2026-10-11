using Microsoft.AspNetCore.Mvc;
using ThesisDemo.Application.Search;
using ThesisDemo.Infrastructure.Legacy;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/collections")]
public sealed class SearchController(ISearchService search) : ControllerBase
{
    [HttpPost("{id:guid}/search")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search(Guid id, [FromBody] SearchRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await search.SearchAsync(id, request, ct));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return BadRequest(new { code = "INVALID_SEARCH_REQUEST", detail = ex.Message, traceId = HttpContext.TraceIdentifier });
        }
        catch (KeyNotFoundException)
        {
            return Problem("Collection was not found.", statusCode: StatusCodes.Status404NotFound, title: "Not found", extensions: ProblemExtensions("COLLECTION_NOT_FOUND"));
        }
        catch (FileNotFoundException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Index not ready", extensions: ProblemExtensions("INDEX_NOT_READY"));
        }
        catch (PythonSearchException ex)
        {
            var status = ex.Code is "INDEX_NOT_LOADED" ? StatusCodes.Status409Conflict : StatusCodes.Status503ServiceUnavailable;
            return Problem(ex.Message, statusCode: status, title: "Search worker failed", extensions: ProblemExtensions(ex.Code ?? "SEARCH_WORKER_FAILED"));
        }
        catch (TimeoutException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status504GatewayTimeout, title: "Search timed out", extensions: ProblemExtensions("SEARCH_TIMEOUT"));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Search conflict", extensions: ProblemExtensions("SEARCH_CONFLICT"));
        }
    }

    private Dictionary<string, object?> ProblemExtensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = HttpContext.TraceIdentifier
    };
}
