using AutopilotSeo.Web.Crawl;

namespace AutopilotSeo.Tests;

public class RobotsTests
{
    const string Txt = """
        User-agent: Googlebot
        Disallow: /

        User-agent: Bingbot
        User-agent: *
        Disallow: /wp-admin/
        Allow: /wp-admin/admin-ajax.php
        Disallow: /*?s=
        Disallow: /*.pdf$

        Sitemap: https://ex.com/sitemap_index.xml
        """;

    [Theory]
    [InlineData("/", true)]                          // Googlebot-only "Disallow: /" does not apply to *
    [InlineData("/wp-admin/options.php", false)]
    [InlineData("/wp-admin/admin-ajax.php", true)]   // longer Allow wins
    [InlineData("/blog?s=seo", false)]               // wildcard
    [InlineData("/file.pdf", false)]                 // end anchor
    [InlineData("/file.pdf?x=1", true)]
    public void Star_group_rules(string path, bool allowed) => Assert.Equal(allowed, Robots.Parse(Txt).Allowed(path));

    [Fact]
    public void Reads_sitemaps_and_empty_file_allows_all()
    {
        Assert.Equal(["https://ex.com/sitemap_index.xml"], Robots.Parse(Txt).Sitemaps);
        Assert.True(Robots.Parse("").Allowed("/anything"));
    }
}
