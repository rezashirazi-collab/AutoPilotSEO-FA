using System.Text;
using AutopilotSeo.Web.Gsc;

namespace AutopilotSeo.Tests;

public class GoogleApiTests
{
    static string B64Url(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Reads_email_from_unpadded_base64url_payload()
    {
        // Payload length chosen so base64 needs padding, and contains '?' → '_' / '>' → '-' style chars.
        var token = $"{B64Url("{\"alg\":\"RS256\"}")}.{B64Url("{\"email\":\"owner@example.com\",\"x\":\"??>>\"}")}.sig";
        Assert.Equal("owner@example.com", GoogleApi.EmailFromIdToken(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public void Missing_or_malformed_token_gives_empty(string? token) => Assert.Equal("", GoogleApi.EmailFromIdToken(token));
}
