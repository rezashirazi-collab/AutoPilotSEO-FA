using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using AutopilotSeo.Core;

namespace AutopilotSeo.Web.Crawl;

/// <summary>
/// Breadth-first crawler for one site. Stays on the site's host (and its www twin), obeys robots.txt for "*",
/// fetches 4 pages at a time with a short pause. Sitemap URLs that links never reach are fetched last (orphans).
/// </summary>
public sealed class Crawler(HttpClient http, ILogger<Crawler> log)
{
    public const string UserAgent = "SeoKhodranBot/0.1 (+site audit for the site owner)";
    const int MaxHtmlBytes = 3_000_000;
    // Start polite (4 at a time, 300 ms apart); the first 429/503 drops to one request at a time with a longer pause.
    int _parallel = 4;
    TimeSpan _pause = TimeSpan.FromMilliseconds(300);
    static readonly Regex SkipExt = new(@"\.(jpe?g|png|gif|webp|svg|ico|pdf|zip|rar|mp4|mp3|avi|css|js|xml|json|woff2?|ttf|eot|docx?|xlsx?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Link(string From, string To, string Anchor);
    public sealed record Result(List<CrawlPage> Pages, List<Link> Links, bool HitLimit, string HomeUrl);

    public async Task<Result> CrawlAsync(string domain, int maxPages, CancellationToken ct)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { domain, "www." + domain };
        var home = $"https://{domain}/";
        var robots = await RobotsAsync(home, ct);
        var sitemap = await SitemapUrlsAsync(robots.Sitemaps.Count > 0 ? robots.Sitemaps : [$"https://{domain}/sitemap.xml"], hosts, maxPages * 2, ct);

        var pages = new Dictionary<string, CrawlPage>();
        var links = new List<Link>();
        var queue = new Queue<(string Url, int Depth)>();
        queue.Enqueue((home, 0));
        var queued = new HashSet<string> { home };
        var parser = new HtmlParser();

        async Task Drain()
        {
            while (queue.Count > 0 && pages.Count < maxPages)
            {
                var batch = new List<(string Url, int Depth)>();
                while (queue.Count > 0 && batch.Count < _parallel && pages.Count + batch.Count < maxPages) batch.Add(queue.Dequeue());

                var fetched = await Task.WhenAll(batch.Select(b => FetchAsync(b.Url, b.Depth, robots, parser, ct)));
                foreach (var (page, outLinks) in fetched)
                {
                    page.InSitemap = sitemap.Contains(page.Url);
                    pages[page.Url] = page;
                    var next = page.RedirectTo is null ? outLinks : [new Link(page.Url, page.RedirectTo, "")];
                    foreach (var l in next)
                    {
                        if (!hosts.Contains(new Uri(l.To).Host)) continue;
                        if (page.RedirectTo is null) links.Add(l);
                        // A redirect target keeps the depth of the URL that redirected to it.
                        if (queued.Add(l.To)) queue.Enqueue((l.To, page.RedirectTo is null ? page.Depth + 1 : page.Depth));
                    }
                }
                if (pages.Count / 50 != (pages.Count - fetched.Length) / 50)
                    log.LogInformation("Crawl {Domain}: {Pages} pages, {Parallel} at a time", domain, pages.Count, _parallel);
                await Task.Delay(_pause, ct);
            }
        }

        await Drain();
        foreach (var u in sitemap.Where(queued.Add)) queue.Enqueue((u, -1)); // -1: not reachable through links
        await Drain();

        var inlinks = links.Where(l => l.From != l.To).DistinctBy(l => (l.From, l.To)).GroupBy(l => l.To).ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in pages.Values) p.Inlinks = inlinks.GetValueOrDefault(p.Url);
        return new([.. pages.Values], links, queue.Count > 0, home);
    }

    async Task<(CrawlPage Page, List<Link> Links)> FetchAsync(string url, int depth, Robots robots, HtmlParser parser, CancellationToken ct)
    {
        var page = new CrawlPage { Url = url, Depth = depth };
        if (!robots.Allowed(new Uri(url).PathAndQuery)) { page.BlockedByRobots = true; return (page, []); }

        try
        {
            using var res = await GetPoliteAsync(url, page, ct);
            if (res.Headers.TryGetValues("X-Robots-Tag", out var xr) && xr.Any(v => v.Contains("noindex", StringComparison.OrdinalIgnoreCase)))
                page.Noindex = true;

            if (page.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc)
            {
                page.RedirectTo = Normalize(new Uri(new Uri(url), loc));
                return (page, []);
            }
            if (page.StatusCode != 200 || res.Content.Headers.ContentType?.MediaType != "text/html") return (page, []);

            // HttpClient.Timeout stops at the headers with ResponseHeadersRead; the body needs its own limit.
            using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bodyCts.CancelAfter(TimeSpan.FromSeconds(30));
            var html = await ReadLimitedAsync(res, bodyCts.Token);
            return (page, Parse(page, html, parser));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            page.StatusCode = 0; // body stalled
            return (page, []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogDebug(ex, "Fetch failed {Url}", url);
            page.StatusCode = 0;
            return (page, []);
        }
    }

    /// <summary>GET that backs off on 429/503 (Retry-After, capped at 30 s) up to 3 tries and slows the rest of the crawl.</summary>
    async Task<HttpResponseMessage> GetPoliteAsync(string url, CrawlPage page, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var sw = Stopwatch.StartNew();
            var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            page.StatusCode = (int)res.StatusCode;
            page.ResponseMs = (int)sw.ElapsedMilliseconds;
            if (res.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) || attempt == 3) return res;

            _parallel = 1;
            _pause = TimeSpan.FromSeconds(Math.Min(_pause.TotalSeconds * 2 + 1, 5));
            var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
            res.Dispose();
            await Task.Delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, ct);
        }
    }

    static List<Link> Parse(CrawlPage page, string html, HtmlParser parser)
    {
        var doc = parser.ParseDocument(html);
        var baseUri = new Uri(page.Url);
        page.Title = Cut(doc.Title?.Trim(), 512);
        foreach (var m in doc.QuerySelectorAll("meta[name]"))
        {
            var name = m.GetAttribute("name")!.ToLowerInvariant();
            var content = m.GetAttribute("content") ?? "";
            if (name == "description") page.MetaDescription = Cut(content.Trim(), 1024);
            else if ((name == "robots" || name == "googlebot") && content.Contains("noindex", StringComparison.OrdinalIgnoreCase)) page.Noindex = true;
        }
        var canonical = doc.QuerySelectorAll("link[rel][href]")
            .FirstOrDefault(l => l.GetAttribute("rel")!.Split(' ').Contains("canonical", StringComparer.OrdinalIgnoreCase))?.GetAttribute("href");
        if (canonical is not null && Uri.TryCreate(baseUri, canonical, out var cu)) page.Canonical = Normalize(cu);
        page.H1 = Cut(doc.QuerySelector("h1")?.TextContent.Trim(), 512);

        var links = new List<Link>();
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href")!.Trim();
            if (href.StartsWith('#') || !Uri.TryCreate(baseUri, href, out var u) || u.Scheme is not ("http" or "https") || Skip(u)) continue;
            var anchor = a.TextContent.Trim();
            if (anchor == "") anchor = a.QuerySelector("img[alt]")?.GetAttribute("alt") ?? "";
            links.Add(new Link(page.Url, Normalize(u), Cut(Regex.Replace(anchor, @"\s+", " "), 256)!));
        }

        foreach (var junk in doc.QuerySelectorAll("script, style, noscript").ToList()) junk.Remove();
        page.WordCount = (doc.Body?.TextContent ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return links;
    }

    /// <summary>Media files and Cloudflare's /cdn-cgi/ endpoints (e.g. email protection) are not pages.</summary>
    static bool Skip(Uri u) => SkipExt.IsMatch(u.AbsolutePath) || u.AbsolutePath.StartsWith("/cdn-cgi/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Absolute URL without fragment; host lower-cased. Path and query are kept as the site wrote them.</summary>
    public static string Normalize(Uri u)
    {
        var b = new UriBuilder(u) { Fragment = "", Host = u.Host.ToLowerInvariant() };
        if (b.Uri.IsDefaultPort) b.Port = -1;
        return b.Uri.AbsoluteUri;
    }

    static string? Cut(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;

    static async Task<string> ReadLimitedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        await using var s = await res.Content.ReadAsStreamAsync(ct);
        var buf = new byte[MaxHtmlBytes];
        int total = 0, n;
        while (total < buf.Length && (n = await s.ReadAsync(buf.AsMemory(total), ct)) > 0) total += n;
        var enc = System.Text.Encoding.UTF8;
        try { if (res.Content.Headers.ContentType?.CharSet is { } cs) enc = System.Text.Encoding.GetEncoding(cs.Trim('"')); }
        catch (ArgumentException) { } // unknown charset: UTF-8 is right for nearly every site we see
        return enc.GetString(buf, 0, total);
    }

    /// <summary>GET that follows up to 3 redirects; for robots.txt and sitemaps (pages themselves record redirects instead).</summary>
    async Task<string?> GetTextAsync(string url, CancellationToken ct)
    {
        for (var hop = 0; hop < 4; hop++)
        {
            using var res = await http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode is < 300 or >= 400 || res.Headers.Location is null) return null;
            url = new Uri(new Uri(url), res.Headers.Location).AbsoluteUri;
        }
        return null;
    }

    async Task<Robots> RobotsAsync(string home, CancellationToken ct)
    {
        try { return Robots.Parse(await GetTextAsync(new Uri(new Uri(home), "/robots.txt").AbsoluteUri, ct) ?? ""); }
        catch (HttpRequestException) { return Robots.Parse(""); }
    }

    async Task<HashSet<string>> SitemapUrlsAsync(List<string> roots, HashSet<string> hosts, int limit, CancellationToken ct)
    {
        var urls = new HashSet<string>();
        var todo = new Queue<(string Url, int Level)>(roots.Select(r => (r, 0)));
        while (todo.Count > 0 && urls.Count < limit)
        {
            var (sm, level) = todo.Dequeue();
            try
            {
                var text = await GetTextAsync(sm, ct);
                if (text is null) continue;
                var xml = XDocument.Parse(text);
                // Only <url><loc> / <sitemap><loc>: image:loc and video:loc children are media, not pages.
                foreach (var loc in xml.Descendants().Where(e => e.Name.LocalName == "loc" && e.Parent?.Name.LocalName is "url" or "sitemap").Select(e => e.Value.Trim()))
                {
                    if (!Uri.TryCreate(loc, UriKind.Absolute, out var u) || !hosts.Contains(u.Host) || Skip(u)) continue;
                    if (xml.Root?.Name.LocalName == "sitemapindex") { if (level < 2) todo.Enqueue((loc, level + 1)); }
                    else if (urls.Count < limit) urls.Add(Normalize(u));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException) { log.LogInformation("Sitemap unreadable {Url}", sm); }
        }
        return urls;
    }

    public static HttpMessageHandler Handler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, // redirects are recorded, not followed
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}

/// <summary>robots.txt rules for "*": longest matching path wins, Allow wins ties; supports * and $ (RFC 9309).</summary>
public sealed class Robots
{
    readonly List<(Regex Pattern, int Length, bool Allow)> _rules = [];
    public List<string> Sitemaps { get; } = [];

    public static Robots Parse(string text)
    {
        var r = new Robots();
        bool inStar = false, lastWasAgent = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var i = line.IndexOf(':');
            if (i < 0) continue;
            var key = line[..i].Trim().ToLowerInvariant();
            var val = line[(i + 1)..].Trim();
            switch (key)
            {
                case "user-agent":
                    inStar = (lastWasAgent && inStar) || val == "*";
                    lastWasAgent = true;
                    continue;
                case "sitemap":
                    if (Uri.IsWellFormedUriString(val, UriKind.Absolute)) r.Sitemaps.Add(val);
                    break;
                case "allow" or "disallow" when inStar && val != "":
                    var rx = "^" + Regex.Escape(val).Replace(@"\*", ".*").Replace(@"\$", "$");
                    r._rules.Add((new Regex(rx, RegexOptions.CultureInvariant), val.Length, key == "allow"));
                    break;
            }
            lastWasAgent = false;
        }
        return r;
    }

    public bool Allowed(string pathAndQuery)
    {
        var match = _rules.Where(x => x.Pattern.IsMatch(pathAndQuery))
            .OrderByDescending(x => x.Length).ThenByDescending(x => x.Allow).FirstOrDefault();
        return match.Pattern is null || match.Allow;
    }
}
