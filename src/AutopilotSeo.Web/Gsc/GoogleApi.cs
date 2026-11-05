using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutopilotSeo.Web.Gsc;

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool IsConfigured => ClientId != "" && ClientSecret != "";
}

/// <summary>Google rejected the refresh token: revoked, expired (7 days in OAuth "Testing" mode) or access removed.</summary>
public sealed class GoogleAuthRevokedException(string message) : Exception(message);

/// <summary>The few Google OAuth + Search Console endpoints we use, over plain HttpClient.</summary>
public sealed class GoogleApi(HttpClient http, IOptions<GoogleOptions> options)
{
    public const string SearchConsoleScope = "https://www.googleapis.com/auth/webmasters.readonly";
    const string Base = "https://searchconsole.googleapis.com/webmasters/v3";
    public const int MaxRows = 25_000;

    static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    GoogleOptions O => options.Value;

    public sealed record Token(string AccessToken, string? RefreshToken, string? IdToken, string? Scope);
    public sealed record Property(string SiteUrl, string PermissionLevel);
    public sealed record Row(string[] Keys, double Clicks, double Impressions, double Position);

    record SitesResponse(List<Property>? SiteEntry);
    record QueryResponse(List<Row>? Rows);

    public string AuthorizeUrl(string redirectUri, string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = O.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email " + SearchConsoleScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent", // always returns a refresh token, also on reconnect
            ["state"] = state,
        }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

    public Task<Token> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        TokenAsync(new()
        {
            ["code"] = code, ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code",
        }, ct);

    public async Task<string> AccessTokenAsync(string refreshToken, CancellationToken ct = default) =>
        (await TokenAsync(new() { ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token" }, ct)).AccessToken;

    public async Task RevokeAsync(string token)
    {
        try { await http.PostAsync("https://oauth2.googleapis.com/revoke?token=" + Uri.EscapeDataString(token), null); }
        catch (HttpRequestException) { } // best effort: the connection is removed on our side anyway
    }

    async Task<Token> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = O.ClientId;
        form["client_secret"] = O.ClientSecret;
        using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode == HttpStatusCode.BadRequest && body.Contains("invalid_grant"))
            throw new GoogleAuthRevokedException("Google access was revoked or expired.");
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Google token endpoint {(int)res.StatusCode}: {Trim(body)}");
        return JsonSerializer.Deserialize<Token>(body, Snake)!;
    }

    /// <summary>Email claim of an id_token received directly from Google's token endpoint over TLS (so no signature check).</summary>
    public static string EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 }) return "";
        var p = parts[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
        return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    }

    public async Task<List<Property>> ListPropertiesAsync(string accessToken, CancellationToken ct = default)
    {
        var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Base}/sites"), accessToken, ct);
        return JsonSerializer.Deserialize<SitesResponse>(res, JsonSerializerOptions.Web)?.SiteEntry ?? [];
    }

    /// <summary>All rows for the range and dimensions, paging through 25k-row pages. Final (not fresh) data only.</summary>
    public async Task<List<Row>> QueryAllAsync(string accessToken, string property, DateOnly start, DateOnly end, string[] dimensions, CancellationToken ct = default)
    {
        var all = new List<Row>();
        while (true)
        {
            var body = JsonSerializer.Serialize(new
            {
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                dimensions,
                type = "web",
                dataState = "final",
                rowLimit = MaxRows,
                startRow = all.Count,
            });
            var json = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Base}/sites/{Uri.EscapeDataString(property)}/searchAnalytics/query")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }, accessToken, ct);
            var rows = JsonSerializer.Deserialize<QueryResponse>(json, JsonSerializerOptions.Web)?.Rows ?? [];
            all.AddRange(rows);
            if (rows.Count < MaxRows) return all;
        }
    }

    public sealed record IndexStatus(string? Verdict, string? CoverageState, string? IndexingState, string? RobotsTxtState,
        string? PageFetchState, string? GoogleCanonical, string? UserCanonical, DateTime? LastCrawlTime);
    record InspectResponse(InspectResult? InspectionResult);
    record InspectResult(IndexStatus? IndexStatusResult);

    /// <summary>URL Inspection API: 2,000 calls/day and 600/minute per property.</summary>
    public async Task<IndexStatus?> InspectAsync(string accessToken, string property, string url, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { inspectionUrl = url, siteUrl = property, languageCode = "fa" });
        var json = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "https://searchconsole.googleapis.com/v1/urlInspection/index:inspect")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }, accessToken, ct);
        return JsonSerializer.Deserialize<InspectResponse>(json, JsonSerializerOptions.Web)?.InspectionResult?.IndexStatusResult;
    }

    // ponytail: fixed 3-try backoff on 429/5xx; switch to a resilience pipeline if quota errors become common.
    async Task<string> SendAsync(Func<HttpRequestMessage> make, string accessToken, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return body;
            var retryable = res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500;
            if (!retryable || attempt == 3)
                throw new HttpRequestException($"Search Console {(int)res.StatusCode}: {Trim(body)}", null, res.StatusCode);
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
        }
    }

    static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
