using AutopilotSeo.Core;

namespace AutopilotSeo.Tests;

public class DomainsTests
{
    [Theory]
    [InlineData("https://www.Example.com/blog?x=1", "example.com")]
    [InlineData("shop.example.ir", "shop.example.ir")]
    [InlineData("  example.com.  ", "example.com")]
    [InlineData("http://localhost", null)]
    [InlineData("192.168.1.1", null)]
    [InlineData("not a domain", null)]
    [InlineData("", null)]
    public void Normalize(string input, string? expected) => Assert.Equal(expected, Domains.Normalize(input));
}
