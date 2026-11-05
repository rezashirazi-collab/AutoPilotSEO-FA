namespace AutopilotSeo.Core;

public static class Domains
{
    /// <summary>"https://www.Example.com/blog" → "example.com". Returns null for anything that is not a public host name.</summary>
    public static string? Normalize(string? input)
    {
        var s = input?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.Contains("://")) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns) return null;

        var host = uri.Host.ToLowerInvariant().TrimEnd('.');
        if (host.StartsWith("www.")) host = host[4..];
        return host.Contains('.') ? host : null;
    }
}
