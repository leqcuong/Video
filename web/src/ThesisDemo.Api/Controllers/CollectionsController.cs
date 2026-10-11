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
            return ProblemWithCode("PRECONDITION_REQUIRED", "If-Match is required.", StatusCodes.Status428PreconditionRequired, "Precondition required");
        try
        {
            var collection = await collections.UpdateAsync(id, command with { IfMatch = ifMatch.ToString() }, ct);
            Response.Headers.ETag = Quote(collection.Version);
            return Ok(collection);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProblemWithCode("STALE_VERSION", "The collection has changed.", StatusCodes.Status412PreconditionFailed, "Precondition failed");
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
            return ProblemWithCode("IMPORT_CONFLICT", ex.Message, StatusCodes.Status409Conflict, "Import conflict");
        }
        catch (ArgumentException ex)
        {
            return ProblemWithCode("INVALID_IMPORT_REQUEST", ex.Message, StatusCodes.Status400BadRequest, "Invalid import request");
        }
    }

    [HttpGet("{id:guid}/assets")]
    public async Task<ActionResult<PageResult<AssetDto>>> Assets(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? state = null, [FromQuery] string? name = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(state) && !Enum.TryParse<ThesisDemo.Domain.Library.AssetState>(state, true, out _))
            return ProblemWithCode("INVALID_ASSET_STATE", "Unsupported asset state.", StatusCodes.Status400BadRequest, "Invalid filter");
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
        ProblemWithCode(code, detail, StatusCodes.Status404NotFound, "Not found");

    private ObjectResult ProblemWithCode(string code, string detail, int statusCode, string title)
    {
        var result = Problem(detail, statusCode: statusCode, title: title);
        ((ProblemDetails)result.Value!).Extensions["code"] = code;
        return result;
    }

    private static string Quote(string value) => "\"" + value + "\"";
}
