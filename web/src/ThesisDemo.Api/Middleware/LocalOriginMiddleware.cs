namespace ThesisDemo.Api.Middleware;

public sealed class LocalOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
        var hasOrigin = context.Request.Headers.TryGetValue("Origin", out var origin);
        var validOrigin = hasOrigin && Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            uri.Scheme == context.Request.Scheme && uri.Host.Equals(context.Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
            uri.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80));
        if (mutation && hasOrigin && !validOrigin)
        {
            await Results.Problem(statusCode: 403, title: "Forbidden origin", type: "about:blank",
                extensions: new Dictionary<string, object?> { ["code"] = "ORIGIN_REJECTED", ["traceId"] = context.TraceIdentifier })
                .ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
