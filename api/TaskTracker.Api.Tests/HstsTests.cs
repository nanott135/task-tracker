using Xunit;

namespace TaskTracker.Api.Tests;

public class HstsTests
{
    // HstsMiddleware skips adding the header for localhost/loopback hosts
    // by design (so local development is never HSTS-pinned in a browser),
    // so these requests target a non-localhost host to exercise it.
    private const string NonLocalhostUrl = "https://tasktracker.example/api/tasks";

    [Fact]
    public async Task Production_HttpsResponse_IncludesStrictTransportSecurityHeader()
    {
        using var factory = new ApiWebApplicationFactory("Production");
        var client = factory.CreateClient();

        var response = await client.GetAsync(NonLocalhostUrl);

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_HttpsResponse_OmitsStrictTransportSecurityHeader()
    {
        using var factory = new ApiWebApplicationFactory("Development");
        var client = factory.CreateClient();

        var response = await client.GetAsync(NonLocalhostUrl);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
