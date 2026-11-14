using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using HongxueAssistant.Core.Services;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

[Collection("Update environment")]
public class GitHubUpdateTransportTests
{
    private static readonly GitHubUpdateRoute Direct = new("direct");
    private static readonly GitHubUpdateRoute Proxy = new("proxy", "https://proxy.example");
    private const string Package = "https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/HongxueAssistant-v99.0.0-win-x64.zip";
    private const string Checksums = "https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/SHA256SUMS.txt";
    private const string Release = """
        {"tag_name":"v99.0.0","body":"release","assets":[
          {"name":"HongxueAssistant-v99.0.0-win-x64.zip","browser_download_url":"https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/HongxueAssistant-v99.0.0-win-x64.zip"},
          {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/SHA256SUMS.txt"}]}
        """;

    [Fact]
    public void ProxyUsesActualSourcePathAndRejectsNonPublicGithubUrls()
    {
        Proxy.Resolve(UpdateService.DefaultFeedUrl).Should().Be("https://proxy.example/" + UpdateService.DefaultFeedUrl);
        Proxy.Resolve(Package).Should().Be("https://proxy.example/" + Package);
        Action localhost = () => Proxy.Resolve("http://127.0.0.1/private");
        Action credentials = () => Proxy.Resolve("https://secret@github.com/example");
        localhost.Should().Throw<ArgumentException>();
        credentials.Should().Throw<ArgumentException>();
        GitHubUpdateTransport.IsRepositoryAssetUrl(Package).Should().BeTrue();
        GitHubUpdateTransport.IsRepositoryAssetUrl("https://github.com/other/repo/releases/download/v1/a.zip").Should().BeFalse();
    }

    [Fact]
    public async Task ProbeDistinguishesNoReleaseFromFailureAndReadsOnlyZipHeader()
    {
        var urls = new ConcurrentQueue<string>();
        using var handler = new StubHandler((request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            urls.Enqueue(url);
            if (url.EndsWith("/releases/latest")) return Task.FromResult(Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound));
            if (url.EndsWith("/HongxueAssistant")) return Task.FromResult(Json("{\"full_name\":\"friends-xiaohuli/HongxueAssistant\"}"));
            request.Headers.Range!.ToString().Should().Be("bytes=0-3");
            return Task.FromResult(Binary([80, 75, 3, 4], HttpStatusCode.PartialContent));
        });
        using var http = new HttpClient(handler);
        var reports = await new GitHubUpdateTransport(http, [Direct, Proxy]).ProbeAsync(UpdateService.DefaultFeedUrl);
        reports.Should().HaveCount(2).And.OnlyContain(report => report.ApiState == UpdateRouteState.NoRelease
            && report.DownloadState == UpdateRouteState.Available && report.UsesPublicSample);
        reports.Should().OnlyContain(report => report.DownloadUrl!.Contains("/benbuck/rbtray/releases/download/"));
        urls.Should().HaveCount(6);
        urls.Should().NotContain("https://proxy.example/");
    }

    [Fact]
    public async Task DownloadOnlyProxyCanBeAvailableWhenItsApiIsForbidden()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri.Contains("api.github.com")
            ? Json("{\"message\":\"Forbidden\"}", HttpStatusCode.Forbidden)
            : Binary([80, 75, 3, 4], HttpStatusCode.PartialContent)));
        using var http = new HttpClient(handler);
        var reports = await new GitHubUpdateTransport(http, [Direct, Proxy]).ProbeAsync(UpdateService.DefaultFeedUrl);
        reports.Should().OnlyContain(report => report.ApiState == UpdateRouteState.Failed
            && report.ApiDetail.Contains("403") && report.DownloadState == UpdateRouteState.Available);
    }

    [Fact]
    public async Task HtmlWithStatus200IsNeverReportedAsWorkingApiOrDownload()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Html("<html>proxy challenge</html>")));
        using var http = new HttpClient(handler);
        var reports = await new GitHubUpdateTransport(http, [Direct, Proxy]).ProbeAsync(UpdateService.DefaultFeedUrl);
        reports.Should().OnlyContain(report => report.ApiState == UpdateRouteState.Failed
            && report.DownloadState == UpdateRouteState.Failed);
    }

    [Fact]
    public async Task Json404NeedsMatchingRepositoryBeforeClaimingNoRelease()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound)));
        using var http = new HttpClient(handler);
        var reports = await new GitHubUpdateTransport(http, [Direct]).ProbeAsync(UpdateService.DefaultFeedUrl);
        reports.Single().ApiState.Should().Be(UpdateRouteState.Failed);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"not an object\"")]
    [InlineData("{\"message\":42}")]
    public async Task Malformed404PayloadsAreNotMistakenForMissingReleases(string payload)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri.Contains("api.github.com")
            ? Json(payload, HttpStatusCode.NotFound) : Binary([80, 75, 3, 4])));
        using var http = new HttpClient(handler);
        var results = await new GitHubUpdateTransport(http, [Direct, Proxy]).ProbeAsync(UpdateService.DefaultFeedUrl);
        results.Should().OnlyContain(result => result.ApiState == UpdateRouteState.Failed && result.DownloadState == UpdateRouteState.Available);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v99.0.0\",\"assets\":[null]}")]
    [InlineData("{\"tag_name\":\"v99.0.0\",\"assets\":[42]}")]
    [InlineData("{\"tag_name\":42,\"assets\":[]}")]
    [InlineData("{\"tag_name\":\"v99.0.0\",\"assets\":null}")]
    [InlineData("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":null,\"browser_download_url\":\"x\"}]}")]
    [InlineData("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"x\",\"browser_download_url\":null}]}")]
    public async Task InvalidReleaseShapesFallBackWithoutCrashingTheWholeProbe(string payload)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri.Contains("api.github.com")
            ? Json(payload) : Binary([80, 75, 3, 4])));
        using var http = new HttpClient(handler);
        var transport = new GitHubUpdateTransport(http, [Direct, Proxy]);
        var results = await transport.ProbeAsync(UpdateService.DefaultFeedUrl);
        results.Should().OnlyContain(result => result.ApiState == UpdateRouteState.Failed && result.DownloadState == UpdateRouteState.Available);
    }

    [Fact]
    public async Task LocalFileCreationFailureDoesNotRetryAnotherNetworkRoute()
    {
        var requestCount = 0;
        using var handler = new StubHandler((_, _) => { Interlocked.Increment(ref requestCount); return Task.FromResult(Binary([80, 75, 3, 4])); });
        using var http = new HttpClient(handler);
        var transport = new GitHubUpdateTransport(http, [Direct, Proxy]);
        var missingDirectoryPath = Path.Combine(Path.GetTempPath(), "missing-update-test-" + Guid.NewGuid().ToString("N"), "package.zip");
        Func<Task> download = () => transport.DownloadPackageAsync(Package, missingDirectoryPath);
        await download.Should().ThrowAsync<IOException>().WithMessage("*写入更新安装包失败*");
        requestCount.Should().Be(1);
    }

    [Fact]
    public async Task ProbeUsesActualReleaseAssetWhenAvailableInsteadOfSample()
    {
        var requests = new ConcurrentQueue<string>();
        using var handler = new StubHandler((request, _) =>
        {
            requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(request.RequestUri.AbsoluteUri.Contains("api.github.com") ? Json(Release) : Binary([80, 75, 3, 4]));
        });
        using var http = new HttpClient(handler);
        var reports = await new GitHubUpdateTransport(http, [Direct]).ProbeAsync(UpdateService.DefaultFeedUrl);
        reports.Single().UsesPublicSample.Should().BeFalse();
        reports.Single().DownloadUrl.Should().Be(Package);
        requests.Should().Equal(UpdateService.DefaultFeedUrl, Package);
    }

    [Fact]
    public async Task ReadReleasePrefersDirectAndDoesNotContactProxiesWhenNoReleaseExists()
    {
        var requests = new ConcurrentQueue<string>();
        using var handler = new StubHandler((request, _) =>
        {
            requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(request.RequestUri.AbsoluteUri.EndsWith("/releases/latest")
                ? Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound)
                : Json("{\"full_name\":\"friends-xiaohuli/HongxueAssistant\"}"));
        });
        using var http = new HttpClient(handler);
        var result = await new GitHubUpdateTransport(http, [Direct, Proxy]).ReadReleaseAsync(UpdateService.DefaultFeedUrl);
        result.NoRelease.Should().BeTrue();
        result.RouteName.Should().Be("direct");
        requests.Should().HaveCount(2).And.OnlyContain(url => url.StartsWith("https://api.github.com/"));
    }

    [Fact]
    public async Task ReadReleaseFallsBackToProxyButKeepsOriginalRepositoryAssetUrls()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Json("{\"message\":\"rate limited\"}", HttpStatusCode.Forbidden) : Json(Release)));
        using var http = new HttpClient(handler);
        var transport = new GitHubUpdateTransport(http, [Direct, Proxy]);
        var result = await transport.ReadReleaseAsync(UpdateService.DefaultFeedUrl);
        result.RouteName.Should().Be("proxy");
        result.DownloadUrl.Should().Be(Package);
        result.Json.Should().Contain("friends-xiaohuli/HongxueAssistant/releases/download/");
    }

    [Fact]
    public async Task ReleasePointingToOtherRepositoryIsRejected()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json(Release.Replace("friends-xiaohuli/HongxueAssistant/releases/download/", "other/project/releases/download/"))));
        using var http = new HttpClient(handler);
        var transport = new GitHubUpdateTransport(http, [Direct]);
        Func<Task> read = () => transport.ReadReleaseAsync(UpdateService.DefaultFeedUrl);
        await read.Should().ThrowAsync<UpdateRouteException>();
    }

    [Fact]
    public async Task ProbeBoundsConcurrencyAndReturnsTimedOutForEveryRoute()
    {
        var active = 0;
        var maximum = 0;
        using var handler = new StubHandler(async (_, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, count);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(Release); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var http = new HttpClient(handler);
        var routes = new[] { Direct }.Concat(Enumerable.Range(0, 6).Select(index => new GitHubUpdateRoute($"proxy{index}", $"https://proxy{index}.example"))).ToArray();
        var transport = new GitHubUpdateTransport(http, routes, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(2), 2);
        var results = await transport.ProbeAsync(UpdateService.DefaultFeedUrl);
        maximum.Should().BeLessThanOrEqualTo(2);
        results.Should().HaveCount(routes.Length);
        results.Should().OnlyContain(report => report.ApiState == UpdateRouteState.TimedOut && report.DownloadState == UpdateRouteState.TimedOut);
    }

    [Fact]
    public async Task UserCancellationStopsProbe()
    {
        using var handler = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(Release); });
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var transport = new GitHubUpdateTransport(http, [Direct, Proxy]);
        Func<Task> probe = () => transport.ProbeAsync(UpdateService.DefaultFeedUrl, ct: cancellation.Token);
        await probe.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadRetriesHtmlResponseAndKeepsZipBytesIntact()
    {
        using var zipBytes = new MemoryStream();
        using (var archive = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("fixture.txt").Open());
            writer.Write("non-executable fixture");
        }
        var bytes = zipBytes.ToArray();
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "github.com" ? Html("blocked") : Binary(bytes)));
        using var http = new HttpClient(handler);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var transport = new GitHubUpdateTransport(http, [Direct, Proxy]);
            await transport.DownloadPackageAsync(Package, path);
            File.ReadAllBytes(path).Should().Equal(bytes);
            transport.LastDownloadRouteName.Should().Be("proxy");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ChecksumsRemainDirectFirstAndHtmlIsRejected()
    {
        var requests = new ConcurrentQueue<string>();
        var checksum = new string('a', 64) + "  test.zip\n";
        using var handler = new StubHandler((request, _) => { requests.Enqueue(request.RequestUri!.AbsoluteUri); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksum) }); });
        using var http = new HttpClient(handler);
        var result = await new GitHubUpdateTransport(http, [Direct, Proxy]).ReadChecksumsAsync(Checksums);
        result.Should().Be(checksum);
        requests.Should().ContainSingle().Which.Should().Be(Checksums);
    }

    [Fact]
    public async Task UpdateServiceReportsNoPublishedVersionWithoutCallingItNetworkFailure()
    {
        var previous = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, null);
            using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri.EndsWith("/releases/latest")
                ? Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound)
                : Json("{\"full_name\":\"friends-xiaohuli/HongxueAssistant\"}")));
            using var http = new HttpClient(handler);
            var service = new UpdateService(http, [Direct, Proxy]);
            var info = await service.CheckForUpdateAsync();
            info.Should().BeNull();
            service.LastError.Should().BeNull();
            service.LastNotice.Should().Contain("暂无发布版本");
        }
        finally { Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, previous); }
    }

    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Html(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Binary(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new ByteArrayContent(bytes) };
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            int current;
            do { current = Volatile.Read(ref target); if (current >= value) return; }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }
}
