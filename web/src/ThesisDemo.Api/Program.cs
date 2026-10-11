using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using ThesisDemo.Api.Components;
using ThesisDemo.Api.Middleware;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Infrastructure.Jobs;
using ThesisDemo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Host:Url"] ?? "http://127.0.0.1:5074");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllersWithViews().ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
{
    var fields = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
        .ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
    return new ObjectResult(new ProblemDetails
    {
        Type = "about:blank", Title = "Validation failed", Status = 400,
        Instance = context.HttpContext.Request.Path,
        Extensions = { ["code"] = "VALIDATION_FAILED", ["traceId"] = context.HttpContext.TraceIdentifier, ["fieldErrors"] = fields }
    }) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (!context.ProblemDetails.Extensions.ContainsKey("code")) context.ProblemDetails.Extensions["code"] = $"HTTP_{context.ProblemDetails.Status}";
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddThesisPersistence(builder.Configuration);
builder.Services.AddScoped<OperationScope>();
builder.Services.AddScoped<CollectionOverview>();
builder.Services.AddScoped<IDurableJobRepository, DurableJobRepository>();
builder.Services.AddSingleton<JobDispatcher>();
builder.Services.AddSingleton<IJobDispatcher>(sp => sp.GetRequiredService<JobDispatcher>());
builder.Services.AddScoped<IJobHandler, NoOpJobHandler>();
builder.Services.AddHostedService<DurableJobWorker>();

var app = builder.Build();
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ThesisDemoDbContext>>().CreateDbContextAsync())
    await db.Database.MigrateAsync();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<LocalOriginMiddleware>();
app.UseAntiforgery();
app.MapControllers();
app.MapGet("/api/v1/security/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken, headerName = "X-CSRF-TOKEN" });
});
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public sealed class NoOpJobHandler : IJobHandler
{
    public Task ExecuteAsync(Guid jobId, Guid claimToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No processing bridge is registered yet.");
}
public partial class Program { }
