using System.Net;

namespace ThesisDemo.Api.Middleware;

public static class LoopbackHost
{
    public static string Validate(string? hostUrl)
    {
        hostUrl ??= "http://127.0.0.1:5074";
        if (!Uri.TryCreate(hostUrl, UriKind.Absolute, out var configuredHost) ||
            configuredHost.Scheme is not ("http" or "https") ||
            !(configuredHost.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              (IPAddress.TryParse(configuredHost.Host, out var address) && IPAddress.IsLoopback(address))))
            throw new InvalidOperationException("Host:Url must bind to localhost or a loopback IP address.");
        return hostUrl;
    }
}
