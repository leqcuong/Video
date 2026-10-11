using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Application.Collections;

namespace ThesisDemo.Api.Controllers;

[ApiController]
[Route("api/v1/collections")]
public sealed class CollectionsController(CollectionHandlers collections, CollectionImportHandlers imports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PageResult<CollectionDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await collections.ListAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CollectionDto>> Get(Guid id, CancellationToken ct)
    {
        var collection = await collections.GetAsync(id, ct);
        if (collection is null) return NotFoundProblem("COLLECTION_NOT_FOUND", "Collection was not found.");
        Response.Headers.ETag = Quote(collection.Version);
        return Ok(collection);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CollectionDto>> Create([FromBody] CreateCollectionCommand command, CancellationToken ct)
    {
        var collection = await collections.CreateAsync(command, ct);
        Response.Headers.ETag = Quote(collection.Version);
        return CreatedAtAction(nameof(Get), new { id = collection.Id }, collection);
    }

    [HttpPatch("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CollectionDto>> Update(Guid id, [FromBody] UpdateCollectionCommand command, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return Problem("If-Match is required.", statusCode: StatusCodes.Status428PreconditionRequired, title: "Precondition required", extensions: ProblemExtensions("PRECONDITION_REQUIRED"));
        try
        {
            var collection = await collections.UpdateAsync(id, command with { IfMatch = ifMatch.ToString() }, ct);
            Response.Headers.ETag = Quote(collection.Version);
            return Ok(collection);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("The collection has changed.", statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition failed", extensions: ProblemExtensions("STALE_VERSION"));
        }
        catch (KeyNotFoundException)
        {
            return NotFoundProblem("COLLECTION_NOT_FOUND", "Collection was not found.");
        }
    }

    [HttpPost("{id:guid}/imports")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(Guid id, [FromBody] ImportCollectionCommand command, CancellationToken ct)
    {
        try
        {
            var result = await imports.HandleAsync(id, command, ct);
            return command.Action == ImportAction.Commit ? Ok(result) : Accepted(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem("SOURCE_OR_COLLECTION_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Import conflict", extensions: ProblemExtensions("IMPORT_CONFLICT"));
        }
        catch (ArgumentException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid import request", extensions: ProblemExtensions("INVALID_IMPORT_REQUEST"));
        }
    }

    [HttpGet("{id:guid}/assets")]
    public async Task<ActionResult<PageResult<AssetDto>>> Assets(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? state = null, [FromQuery] string? name = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(state) && !Enum.TryParse<ThesisDemo.Domain.Library.AssetState>(state, true, out _))
            return Problem("Unsupported asset state.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid filter", extensions: ProblemExtensions("INVALID_ASSET_STATE"));
        var parsedState = string.IsNullOrWhiteSpace(state)
            ? (ThesisDemo.Domain.Library.AssetState?)null
            : Enum.Parse<ThesisDemo.Domain.Library.AssetState>(state, true);
        try
        {
            return Ok(await collections.ListAssetsAsync(id, new ListAssetsQuery(page, pageSize, parsedState, name), ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFoundProblem("COLLECTION_NOT_FOUND", "Collection was not found.");
        }
    }

    private ActionResult NotFoundProblem(string code, string detail) =>
        Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found", extensions: ProblemExtensions(code));

    private Dictionary<string, object?> ProblemExtensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = HttpContext.TraceIdentifier
    };

    private static string Quote(string value) => "\"" + value + "\"";
}
