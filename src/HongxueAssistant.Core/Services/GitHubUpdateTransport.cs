using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HongxueAssistant.Core.Services;

public sealed record GitHubUpdateRoute(string Name, string? Prefix = null)
{
    public bool IsDirect => Prefix is null;

    public string Resolve(string sourceUrl)
    {
        if (IsDirect) return sourceUrl;
        if (!GitHubUpdateTransport.IsPublicGitHubUrl(sourceUrl))
            throw new ArgumentException("仅公开 GitHub 资源可使用更新代理。", nameof(sourceUrl));
        return Prefix!.TrimEnd('/') + "/" + sourceUrl;
    }
}

public enum UpdateRouteState { Available, NoRelease, NotTested, Failed, TimedOut }

public sealed record UpdateRouteProbeResult(
    GitHubUpdateRoute Route, UpdateRouteState ApiState, string ApiDetail,
    UpdateRouteState DownloadState, string DownloadDetail, long ElapsedMilliseconds,
    string ApiUrl, string? DownloadUrl, bool UsesPublicSample)
{
    public string ApiSummary => $"版本接口：{ApiDetail}";
    public string DownloadSummary => $"{(UsesPublicSample ? "公共下载样本" : "安装包下载")}：{DownloadDetail}";
    public string TimingSummary => $"耗时 {ElapsedMilliseconds / 1000d:0.0} 秒";
    public string TestedUrls => $"{ApiUrl}\n{DownloadUrl}";
}

public sealed record GitHubReleaseReadResult(string? Json, bool NoRelease, string RouteName, string? DownloadUrl);
public sealed class UpdateRouteException(string message) : HttpRequestException(message);

/// <summary>Only forwards public GitHub URLs. A proxy homepage status is never used as a health check.</summary>
public sealed class GitHubUpdateTransport
{
    public const string PublicDownloadSampleUrl =
        "https://github.com/benbuck/rbtray/releases/download/v4.14/x64.zip";

    public static IReadOnlyList<GitHubUpdateRoute> DefaultRoutes { get; } = Array.AsReadOnly(new[]
    {
        new GitHubUpdateRoute("GitHub 直连"),
        new GitHubUpdateRoute("github.xxlab.tech", "https://github.xxlab.tech"),
        new GitHubUpdateRoute("cors.isteed.cc", "https://cors.isteed.cc"),
        new GitHubUpdateRoute("ghfast.top", "https://ghfast.top"),
        new GitHubUpdateRoute("github.chenc.dev", "https://github.chenc.dev"),
        new GitHubUpdateRoute("gh-proxy.com", "https://gh-proxy.com"),
        new GitHubUpdateRoute("gh-proxy.org", "https://gh-proxy.org"),
        new GitHubUpdateRoute("fastgit.cc", "https://fastgit.cc"),
        new GitHubUpdateRoute("gh.noki.icu", "https://gh.noki.icu"),
        new GitHubUpdateRoute("ghproxy.net", "https://ghproxy.net"),
        new GitHubUpdateRoute("githubproxy.gohj99.site", "https://githubproxy.gohj99.site"),
        new GitHubUpdateRoute("GH-Proxy CDN（同服务节点）", "https://cdn.gh-proxy.org"),
        new GitHubUpdateRoute("GH-Proxy 香港（同服务节点）", "https://hk.gh-proxy.org"),
        new GitHubUpdateRoute("GH-Proxy EdgeOne（同服务节点）", "https://edgeone.gh-proxy.org"),
    });

    private readonly HttpClient _http;
    private readonly IReadOnlyList<GitHubUpdateRoute> _routes;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _probeTimeout;
    private readonly int _maxConcurrency;
    private IReadOnlyList<UpdateRouteProbeResult> _lastProbeResults = [];

    public GitHubUpdateTransport(HttpClient http, IReadOnlyList<GitHubUpdateRoute>? routes = null,
        TimeSpan? requestTimeout = null, TimeSpan? probeTimeout = null, int maxConcurrency = 4)
    {
        _http = http;
        _routes = routes ?? DefaultRoutes;
        if (_routes.Count == 0 || _routes.Count > 32 || _routes.Count(route => route.IsDirect) != 1)
            throw new ArgumentException("更新线路必须包含一个直连，且最多 32 项。", nameof(routes));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(6);
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(45);
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 8);
    }

    public string? LastApiRouteName { get; private set; }
    public string? LastDownloadRouteName { get; private set; }

    public static bool IsPublicGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.Equals("api.github.com", StringComparison.OrdinalIgnoreCase));

    public async Task<GitHubReleaseReadResult> ReadReleaseAsync(string feedUrl, CancellationToken ct = default)
    {
        LastApiRouteName = null;
        var direct = _routes.Single(route => route.IsDirect);
        try
        {
            var result = await ReadReleaseOnRouteAsync(direct, feedUrl, ct);
            LastApiRouteName = direct.Name;
            return result;
        }
        catch (Exception ex) when (IsRouteFailure(ex) && !ct.IsCancellationRequested)
        {
            if (!IsPublicGitHubUrl(feedUrl)) throw;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        using var gate = new SemaphoreSlim(_maxConcurrency);
        var failures = new List<string>();
        var attempts = _routes.Where(route => !route.IsDirect).Select(async route =>
        {
            try
            {
                await gate.WaitAsync(budget.Token);
                try { return (Result: await ReadReleaseOnRouteAsync(route, feedUrl, budget.Token), Error: (string?)null); }
                finally { gate.Release(); }
            }
            catch (Exception ex) when (IsRouteFailure(ex))
            {
                return (Result: (GitHubReleaseReadResult?)null, Error: $"{route.Name}：{DescribeFailure(ex)}");
            }
        }).ToList();
        var allAttempts = attempts.ToArray();
        while (attempts.Count > 0)
        {
            var completed = await Task.WhenAny(attempts);
            attempts.Remove(completed);
            var attempt = await completed;
            if (attempt.Result is not null)
            {
                ct.ThrowIfCancellationRequested();
                budget.Cancel();
                await Task.WhenAll(allAttempts);
                LastApiRouteName = attempt.Result.RouteName;
                return attempt.Result;
            }
            if (attempt.Error is not null) failures.Add(attempt.Error);
        }
        ct.ThrowIfCancellationRequested();
        throw new UpdateRouteException("直连及代理均未能读取版本接口。" + string.Join("；", failures.Take(4)));
    }

    public async Task<IReadOnlyList<UpdateRouteProbeResult>> ProbeAsync(
        string feedUrl, string? downloadUrl = null, IProgress<UpdateRouteProbeResult>? progress = null,
        CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_probeTimeout);
        using var gate = new SemaphoreSlim(_maxConcurrency);
        var tasks = _routes.Select(async route =>
        {
            UpdateRouteProbeResult result;
            var watch = Stopwatch.StartNew();
            try
            {
                await gate.WaitAsync(budget.Token);
                // Measure this route, not time spent waiting behind earlier probes.
                watch.Restart();
                try { result = await ProbeRouteAsync(route, feedUrl, downloadUrl, watch, budget.Token); }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = new(route, UpdateRouteState.TimedOut, "测试总时限已到", UpdateRouteState.NotTested,
                    "未完成", watch.ElapsedMilliseconds, route.Resolve(feedUrl), null, false);
            }
            progress?.Report(result);
            return result;
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();
        _lastProbeResults = results;
        return results;
    }

    private async Task<UpdateRouteProbeResult> ProbeRouteAsync(GitHubUpdateRoute route, string feedUrl,
        string? suppliedDownloadUrl, Stopwatch watch, CancellationToken ct)
    {
        var apiState = UpdateRouteState.Failed;
        var apiDetail = "未完成";
        string? packageUrl = suppliedDownloadUrl;
        try
        {
            var release = await ReadReleaseOnRouteAsync(route, feedUrl, ct);
            apiState = release.NoRelease ? UpdateRouteState.NoRelease : UpdateRouteState.Available;
            apiDetail = release.NoRelease ? "可访问，仓库暂无发布版本" : "可用（发布 JSON 已验证）";
            packageUrl ??= release.DownloadUrl;
        }
        catch (Exception ex) when (IsRouteFailure(ex) && !ct.IsCancellationRequested)
        {
            apiState = ex is OperationCanceledException ? UpdateRouteState.TimedOut : UpdateRouteState.Failed;
            apiDetail = DescribeFailure(ex);
        }
        ct.ThrowIfCancellationRequested();
        var usesSample = packageUrl is null;
        packageUrl ??= PublicDownloadSampleUrl;
        var downloadState = UpdateRouteState.Failed;
        string downloadDetail;
        try
        {
            using var timeout = RequestToken(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, route.Resolve(packageUrl));
            request.Headers.Range = new RangeHeaderValue(0, 3);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            RejectHtml(response);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            await ReadZipSignatureAsync(stream, timeout.Token);
            downloadState = UpdateRouteState.Available;
            downloadDetail = "可用（仅验证 ZIP 文件头）";
        }
        catch (Exception ex) when (IsRouteFailure(ex) && !ct.IsCancellationRequested)
        {
            downloadState = ex is OperationCanceledException ? UpdateRouteState.TimedOut : UpdateRouteState.Failed;
            downloadDetail = DescribeFailure(ex);
        }
        return new(route, apiState, apiDetail, downloadState, downloadDetail, watch.ElapsedMilliseconds,
            route.Resolve(feedUrl), route.Resolve(packageUrl), usesSample);
    }

    private async Task<GitHubReleaseReadResult> ReadReleaseOnRouteAsync(
        GitHubUpdateRoute route, string feedUrl, CancellationToken ct)
    {
        using var timeout = RequestToken(ct);
        using var response = await _http.GetAsync(route.Resolve(feedUrl), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound && IsDefaultFeed(feedUrl))
        {
            // A 404 at /releases/latest also means an existing repository has no releases.
            // Verify the repository rather than labelling the route dead (or trusting an HTML 404).
            var error = await ReadTextAsync(response, 128 * 1024, timeout.Token);
            using var errorDocument = JsonDocument.Parse(error);
            if (errorDocument.RootElement.ValueKind != JsonValueKind.Object
                || !errorDocument.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.String
                || !string.Equals(message.GetString(), "Not Found", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HTTP 404，未返回 GitHub JSON");
            using var repositoryResponse = await _http.GetAsync(route.Resolve(RepositoryApiUrl), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            repositoryResponse.EnsureSuccessStatusCode();
            RejectHtml(repositoryResponse);
            using var repository = JsonDocument.Parse(await ReadTextAsync(repositoryResponse, 512 * 1024, timeout.Token));
            if (repository.RootElement.ValueKind != JsonValueKind.Object
                || !repository.RootElement.TryGetProperty("full_name", out var name)
                || name.ValueKind != JsonValueKind.String
                || name.GetString() != "friends-xiaohuli/HongxueAssistant")
                throw new InvalidDataException("仓库验证结果不匹配");
            return new(null, true, route.Name, null);
        }
        response.EnsureSuccessStatusCode();
        RejectHtml(response);
        var json = await ReadTextAsync(response, 2 * 1024 * 1024, timeout.Token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tag)
            || tag.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tag.GetString())
            || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("响应不是有效 GitHub 发布信息");
        string? downloadUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out var assetName) || assetName.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(assetName.GetString())
                || !asset.TryGetProperty("browser_download_url", out var assetUrl) || assetUrl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(assetUrl.GetString()))
                throw new InvalidDataException("发布资源列表包含无效条目");
            if (downloadUrl is null && assetName.GetString()!.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                downloadUrl = assetUrl.GetString();
        }
        if (IsDefaultFeed(feedUrl) && downloadUrl is not null && !IsRepositoryAssetUrl(downloadUrl))
            throw new InvalidDataException("发布安装包不属于本仓库的 GitHub Releases");
        return new(json, false, route.Name, downloadUrl);
    }

    public async Task DownloadPackageAsync(string sourceUrl, string zipPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        LastDownloadRouteName = null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        var failures = new List<string>();
        foreach (var route in CandidatesFor(sourceUrl))
        {
            try
            {
                using var headersTimeout = RequestToken(budget.Token);
                using var response = await _http.GetAsync(route.Resolve(sourceUrl), HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                response.EnsureSuccessStatusCode();
                RejectHtml(response);
                headersTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
                using var firstRead = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                firstRead.CancelAfter(TimeSpan.FromSeconds(15));
                var prefix = await ReadZipSignatureAsync(stream, firstRead.Token);
                FileStream file;
                try { file = File.Create(zipPath); }
                catch (IOException ex) { throw new UpdateFileWriteException(ex); }
                try
                {
                    await WriteFileAsync(file, prefix, budget.Token);
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    long downloaded = prefix.Length;
                    var buffer = new byte[65536];
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(15));
                        var count = await stream.ReadAsync(buffer, idle.Token);
                        if (count == 0) break;
                        await WriteFileAsync(file, buffer.AsMemory(0, count), budget.Token);
                        downloaded += count;
                        if (totalBytes > 0) progress?.Report(Math.Min(0.85, (double)downloaded / totalBytes * 0.85));
                    }
                    try { await file.FlushAsync(budget.Token); }
                    catch (IOException ex) { throw new UpdateFileWriteException(ex); }
                    LastDownloadRouteName = route.Name;
                    return;
                }
                finally
                {
                    try { await file.DisposeAsync(); }
                    catch (IOException ex) { throw new UpdateFileWriteException(ex); }
                }
            }
            catch (Exception ex) when (IsRouteFailure(ex) && !ct.IsCancellationRequested)
            {
                failures.Add($"{route.Name}：{DescribeFailure(ex)}");
                if (!IsPublicGitHubUrl(sourceUrl)) throw;
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new UpdateRouteException("安装包下载线路均不可用。" + string.Join("；", failures.Take(4)));
    }

    public async Task<string> ReadChecksumsAsync(string sourceUrl, CancellationToken ct = default, string? assetName = null)
    {
        // Keep the checksum path direct-first even when a proxy downloaded the package.
        foreach (var route in CandidatesFor(sourceUrl))
        {
            try
            {
                using var timeout = RequestToken(ct);
                using var response = await _http.GetAsync(route.Resolve(sourceUrl), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                RejectHtml(response);
                var text = await ReadTextAsync(response, 1024 * 1024, timeout.Token);
                if (text.TrimStart().StartsWith('<')) throw new InvalidDataException("校验清单返回了网页");
                if (assetName is not null && UpdateService.ParseChecksum(text, assetName) is null)
                    throw new InvalidDataException("校验清单缺少目标安装包记录");
                return text;
            }
            catch (Exception ex) when (IsRouteFailure(ex) && !ct.IsCancellationRequested)
            {
                if (!IsPublicGitHubUrl(sourceUrl)) throw;
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new UpdateRouteException("所有线路均未能读取 SHA256 校验清单，已停止更新");
    }

    private IEnumerable<GitHubUpdateRoute> CandidatesFor(string sourceUrl)
    {
        yield return _routes.Single(route => route.IsDirect);
        if (!IsPublicGitHubUrl(sourceUrl)) yield break;
        var available = _lastProbeResults.Where(result => result.DownloadState == UpdateRouteState.Available)
            .OrderBy(result => result.ElapsedMilliseconds).Select(result => result.Route).Where(route => !route.IsDirect).ToArray();
        foreach (var route in available) yield return route;
        foreach (var route in _routes.Where(route => !route.IsDirect && !available.Contains(route))) yield return route;
    }

    public const string RepositoryApiUrl = "https://api.github.com/repos/friends-xiaohuli/HongxueAssistant";
    private static bool IsDefaultFeed(string url) => string.Equals(url, UpdateService.DefaultFeedUrl, StringComparison.Ordinal);
    public static bool IsRepositoryAssetUrl(string url) => IsPublicGitHubUrl(url)
        && new Uri(url).IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && new Uri(url).AbsolutePath.StartsWith("/friends-xiaohuli/HongxueAssistant/releases/download/", StringComparison.Ordinal);

    private CancellationTokenSource RequestToken(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_requestTimeout);
        return timeout;
    }

    private static void RejectHtml(HttpResponseMessage response)
    {
        var media = response.Content.Headers.ContentType?.MediaType;
        if (media is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("返回的是网页，未取得目标资源");
    }

    private static async Task<byte[]> ReadZipSignatureAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        try { await stream.ReadExactlyAsync(prefix, ct); }
        catch (EndOfStreamException ex) { throw new InvalidDataException("响应不足 4 字节，不是 ZIP 文件", ex); }
        if (prefix[0] != 'P' || prefix[1] != 'K'
            || !((prefix[2] == 3 && prefix[3] == 4) || (prefix[2] == 5 && prefix[3] == 6) || (prefix[2] == 7 && prefix[3] == 8)))
            throw new InvalidDataException("响应不是 ZIP 文件，可能是代理提示页");
        return prefix;
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("响应内容超过允许大小");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (bytes.Length + read > limit) throw new InvalidDataException("响应内容超过允许大小");
            bytes.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    private sealed class UpdateFileWriteException(IOException inner)
        : IOException($"写入更新安装包失败：{inner.Message}", inner);

    private static async ValueTask WriteFileAsync(FileStream file, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        try { await file.WriteAsync(bytes, ct); }
        catch (IOException ex) { throw new UpdateFileWriteException(ex); }
    }

    private static bool IsRouteFailure(Exception ex) => ex is not UpdateFileWriteException
        && (ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or JsonException);
    private static string DescribeFailure(Exception ex) => ex switch
    {
        OperationCanceledException => "超时",
        HttpRequestException { StatusCode: not null } http => $"HTTP {(int)http.StatusCode}",
        JsonException => "返回的不是发布 JSON（可能是代理网页）",
        HttpRequestException => "连接失败",
        _ => ex.Message
    };
}
