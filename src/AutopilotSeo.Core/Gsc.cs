namespace AutopilotSeo.Core;

/// <summary>One Search Console connection per site. The refresh token is encrypted by the web app before it gets here.</summary>
public class GscConnection
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string GoogleEmail { get; set; } = "";
    public string ProtectedRefreshToken { get; set; } = "";
    /// <summary>"sc-domain:example.com" or "https://example.com/". Null until the user picks one.</summary>
    public string? Property { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Google rejected the refresh token (revoked, expired testing-mode token…); sync stops until reconnect.</summary>
    public bool NeedsReconnect { get; set; }
}

public enum Device : byte { Desktop, Mobile, Tablet }

public class GscQuery
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Text { get; set; } = "";
}

public class GscPage
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Url { get; set; } = "";
}

/// <summary>Property-level daily totals (include anonymized queries). A row here also marks the day as fully synced.</summary>
public class GscDaily
{
    public int SiteId { get; set; }
    public DateOnly Date { get; set; }
    public int Clicks { get; set; }
    public int Impressions { get; set; }
    public float Position { get; set; }
}

/// <summary>Page-level daily metrics (page aggregation, as Search Console attributes them).</summary>
public class GscPageDaily
{
    public int SiteId { get; set; }
    public DateOnly Date { get; set; }
    public int PageId { get; set; }
    public int Clicks { get; set; }
    public int Impressions { get; set; }
    public float Position { get; set; }
}

public class GscQueryPageDaily
{
    public int SiteId { get; set; }
    public DateOnly Date { get; set; }
    public int QueryId { get; set; }
    public int PageId { get; set; }
    public Device Device { get; set; }
    public int Clicks { get; set; }
    public int Impressions { get; set; }
    public float Position { get; set; }
}

public static class GscRules
{
    /// <summary>Search Console keeps 16 months; we fetch that window and keep history ourselves after it.</summary>
    public static DateOnly WindowStart(DateOnly today) => today.AddMonths(-16).AddDays(1);

    /// <summary>Dates in [start, end] not yet synced, newest first so the dashboard fills from the recent end.</summary>
    public static List<DateOnly> MissingDates(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> synced)
    {
        var list = new List<DateOnly>();
        for (var d = end; d >= start; d = d.AddDays(-1))
            if (!synced.Contains(d)) list.Add(d);
        return list;
    }

    /// <summary>True when a Search Console property covers the site's domain (domain property, or URL-prefix on the host or www.host).</summary>
    public static bool PropertyMatches(string siteDomain, string property)
    {
        if (property.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
            return Domains.Normalize(property[10..]) == siteDomain;
        return Uri.TryCreate(property, UriKind.Absolute, out var u) && Domains.Normalize(u.Host) == siteDomain;
    }
}
