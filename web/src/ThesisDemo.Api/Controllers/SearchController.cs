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
            return ProblemWithCode("COLLECTION_NOT_FOUND", "Collection was not found.", StatusCodes.Status404NotFound, "Not found");
        }
        catch (FileNotFoundException ex)
        {
            return ProblemWithCode("INDEX_NOT_READY", ex.Message, StatusCodes.Status409Conflict, "Index not ready");
        }
        catch (PythonSearchException ex)
        {
            var status = ex.Code is "INDEX_NOT_LOADED" ? StatusCodes.Status409Conflict : StatusCodes.Status503ServiceUnavailable;
            return ProblemWithCode(ex.Code ?? "SEARCH_WORKER_FAILED", ex.Message, status, "Search worker failed");
        }
        catch (TimeoutException ex)
        {
            return ProblemWithCode("SEARCH_TIMEOUT", ex.Message, StatusCodes.Status504GatewayTimeout, "Search timed out");
        }
        catch (InvalidOperationException ex)
        {
            return ProblemWithCode("SEARCH_CONFLICT", ex.Message, StatusCodes.Status409Conflict, "Search conflict");
        }
    }

    private ObjectResult ProblemWithCode(string code, string detail, int statusCode, string title)
    {
        var result = Problem(detail, statusCode: statusCode, title: title);
        ((ProblemDetails)result.Value!).Extensions["code"] = code;
        return result;
    }
}
