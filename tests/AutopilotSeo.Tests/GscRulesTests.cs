using AutopilotSeo.Core;

namespace AutopilotSeo.Tests;

public class GscRulesTests
{
    [Fact]
    public void Window_is_sixteen_months_inclusive()
        => Assert.Equal(new DateOnly(2025, 7, 5), GscRules.WindowStart(new DateOnly(2026, 11, 4)));

    [Fact]
    public void Missing_dates_are_newest_first_and_skip_synced()
    {
        var synced = new HashSet<DateOnly> { new(2026, 11, 2) };
        var missing = GscRules.MissingDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), synced);
        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 1)], missing);
    }

    [Theory]
    [InlineData("example.com", "sc-domain:example.com", true)]
    [InlineData("example.com", "https://www.example.com/", true)]
    [InlineData("example.com", "http://example.com/blog/", true)]
    [InlineData("example.com", "https://shop.example.com/", false)]
    [InlineData("example.com", "sc-domain:other.com", false)]
    [InlineData("shop.example.com", "sc-domain:example.com", false)]
    public void Property_matches_site_domain(string domain, string property, bool expected)
        => Assert.Equal(expected, GscRules.PropertyMatches(domain, property));
}
