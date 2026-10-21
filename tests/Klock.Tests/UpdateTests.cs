using System.Net;
using Klock.Core.Updates;

namespace Klock.Tests;

public class UpdateTests
{
    [Fact]
    public void Manifest_parses()
    {
        var i = UpdateChecker.Parse("""{ "version": "1.2.0", "url": "https://example.com/Setup.exe", "notes": "Fixes" }""");
        Assert.Equal(new Version(1, 2, 0), i!.Latest);
        Assert.Equal("Fixes", i.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "version": "x", "url": "https://a.b/c" }""")]
    [InlineData("""{ "version": "1.0.0" }""")]
    [InlineData("""{ "version": "1.0.0", "url": "http://insecure.example/Setup.exe" }""")]
    [InlineData("""{ "version": "1.0.0", "url": "file:///c:/evil.exe" }""")]
    public void Bad_or_insecure_manifests_are_rejected(string json) => Assert.Null(UpdateChecker.Parse(json));

    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    [Theory]
    [InlineData("1.1.0", UpdateState.Available)]
    [InlineData("1.0.0", UpdateState.UpToDate)]
    [InlineData("0.9.0", UpdateState.UpToDate)]
    public async Task Check_compares_versions(string latest, UpdateState expected)
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, $$"""{ "version": "{{latest}}", "url": "https://x.y/s.exe" }"""));
        var r = await UpdateChecker.CheckAsync(new Version(1, 0, 0), "https://x.y/latest.json", http);
        Assert.Equal(expected, r.State);
    }

    [Fact]
    public async Task Check_fails_softly_on_http_errors_and_garbage()
    {
        using var a = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "oops"));
        Assert.Equal(UpdateState.Failed, (await UpdateChecker.CheckAsync(new Version(1, 0), "https://x.y/l.json", a)).State);
        using var b = new HttpClient(new StubHandler(HttpStatusCode.OK, "<html>"));
        Assert.Equal(UpdateState.Failed, (await UpdateChecker.CheckAsync(new Version(1, 0), "https://x.y/l.json", b)).State);
    }
}
