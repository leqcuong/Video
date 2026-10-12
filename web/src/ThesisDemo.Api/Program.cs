using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using ThesisDemo.Api.Components;
using ThesisDemo.Api.Middleware;
using ThesisDemo.Application.Artifacts;
using ThesisDemo.Application.Collections;
using ThesisDemo.Application.Search;
using ThesisDemo.Infrastructure.Collections;
using ThesisDemo.Infrastructure.Jobs;
using ThesisDemo.Infrastructure.Legacy;
using ThesisDemo.Infrastructure.Persistence;
using ThesisDemo.Infrastructure.Storage;
using ThesisDemo.Infrastructure.Research;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LoopbackHost.Validate(builder.Configuration["Host:Url"]));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllersWithViews().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
}).ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
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
builder.Services.AddScoped<CollectionHandlers>();
builder.Services.AddScoped<CollectionImportHandlers>();
builder.Services.AddScoped<ICollectionStore, EfCollectionStore>();
builder.Services.AddScoped<ILegacyCorpusImporter, LegacyCorpusImporter>();
builder.Services.AddScoped<ISearchService, LegacySearchService>();
builder.Services.AddSingleton<LocalMediaStore>();
builder.Services.AddSingleton<ManagedStorage>();
builder.Services.AddScoped<MediaUploadService>();
builder.Services.AddScoped<SnapshotPublisher>();
builder.Services.AddScoped<PythonPipelineBridge>();
builder.Services.AddScoped<ProcessingCommands>();
builder.Services.AddScoped<ReportImporter>();
builder.Services.AddScoped<EvaluationReadRepository>();
builder.Services.AddScoped<IDurableJobRepository, DurableJobRepository>();
builder.Services.AddSingleton<JobDispatcher>();
builder.Services.AddSingleton<IJobDispatcher>(sp => sp.GetRequiredService<JobDispatcher>());
builder.Services.AddScoped<IJobHandler, ProcessingJobHandler>();
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

public partial class Program { }
