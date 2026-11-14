// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HongxueAssistant.Core.Services;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

/// <summary>
/// 自动更新的纯逻辑：更新源覆盖、版本比较、SHA256 校验、更新脚本生成。
/// 这些用例不访问网络，端到端流程另用本机假 feed 手工验证。
/// </summary>
[Collection("Update environment")]
public class UpdateServiceTests
{
    [Fact]
    public void FeedUrl_UsesEnvironmentOverrideAndFallsBackToGitHub()
    {
        UpdateService.DefaultFeedUrl.Should().Be("https://api.github.com/repos/friends-xiaohuli/HongxueAssistant/releases/latest");
        UpdateService.ResolveFeedUrl(null).Should().Be(UpdateService.DefaultFeedUrl);
        UpdateService.ResolveFeedUrl("   ").Should().Be(UpdateService.DefaultFeedUrl);
        UpdateService.ResolveFeedUrl(" http://127.0.0.1:8123/latest.json ")
            .Should().Be("http://127.0.0.1:8123/latest.json");
        UpdateService.FeedOverrideVariable.Should().Be("HONGXUEASSISTANT_UPDATE_FEED");
    }

    [Theory]
    [InlineData("v1.1.2", "1.1.2")]
    [InlineData("V1.1.2", "1.1.2")]
    [InlineData("1.1.2", "1.1.2")]
    [InlineData("v1.1", "1.1")]
    [InlineData("v1.8.0-alpha1", "1.8.0.1")]
    [InlineData("v1.8.0-alpha2", "1.8.0.2")]
    [InlineData("v1.8.0-alpha10+abcdef", "1.8.0.10")]
    [InlineData("1.8.1-alpha1+build.20261114", "1.8.1.1")]
    [InlineData("v1.8.0-local.1", "1.8.0.1")]
    [InlineData("v1.8.0-local.2+abcdef", "1.8.0.2")]
    public void TryParseVersion_AcceptsReleaseTags(string tag, string expected)
    {
        UpdateService.TryParseVersion(tag)!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("release")]
    [InlineData("v")]
    [InlineData("v1.8.0-alpha")]
    [InlineData("v1.8.0-alpha.1")]
    [InlineData("v1.8.0-alpha-1")]
    [InlineData("v1.8.0-alpha2147483648")]
    [InlineData("v1.8.0-beta1")]
    [InlineData("v1.8.0-rc1")]
    [InlineData("v1.8-alpha1")]
    public void TryParseVersion_RejectsGarbage(string tag)
    {
        UpdateService.TryParseVersion(tag).Should().BeNull();
    }

    [Theory]
    [InlineData("v1.8.0-alpha2", "v1.8.0-alpha1")]
    [InlineData("v1.8.0-alpha10+abcdef", "v1.8.0-alpha2")]
    [InlineData("v1.8.1-alpha1", "v1.8.0-alpha99")]
    [InlineData("v1.9.0-alpha1", "v1.8.99-alpha99")]
    public void TryParseVersion_AlphaNumbersAndUpstreamBaseCompareNumerically(string newer, string older)
    {
        UpdateService.TryParseVersion(newer)!.CompareTo(UpdateService.TryParseVersion(older)).Should().BePositive();
    }

    [Fact]
    public void TryParseVersion_IgnoresBuildMetadataAndPreservesLegacyLocalRevisionCompatibility()
    {
        UpdateService.TryParseVersion("v1.8.0-alpha2+build.abc")
            .Should().Be(UpdateService.TryParseVersion("v1.8.0-alpha2"));
        UpdateService.TryParseVersion("v1.8.0-alpha2")
            .Should().Be(UpdateService.TryParseVersion("v1.8.0-local.2"));
    }

    [Theory]
    [InlineData("../evil-win-x64.zip")]
    [InlineData("..\\evil-win-x64.zip")]
    [InlineData("C:\\evil-win-x64.zip")]
    [InlineData("/evil-win-x64.zip")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("evil:stream-win-x64.zip")]
    [InlineData(" evil-win-x64.zip")]
    [InlineData("CON.-win-x64.zip")]
    public async Task UnsafePackageNamesAreRejectedBeforeNetworkOrTempWrites(string? assetName)
    {
        var requests = 0;
        using var handler = new AssetNameHandler(_ =>
        {
            Interlocked.Increment(ref requests);
            throw new InvalidOperationException("Invalid asset name must never reach the network");
        });
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var before = Directory.GetDirectories(Path.GetTempPath(), "HongxueAssistant-Update-*");
        var info = new UpdateInfo("v99.0.0", "99.0.0", "", "https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/package-win-x64.zip", assetName!, null);
        Func<Task> download = () => service.DownloadAndApplyUpdateAsync(info);
        await download.Should().ThrowAsync<UpdateIntegrityException>().WithMessage("*文件名无效*");
        requests.Should().Be(0);
        Directory.GetDirectories(Path.GetTempPath(), "HongxueAssistant-Update-*").Should().BeEquivalentTo(before);
        service.PendingScriptPath.Should().BeNull();
        service.LastError.Should().Contain("文件名无效");
    }

    [Theory]
    [InlineData("../evil-win-x64.zip")]
    [InlineData("..\\evil-win-x64.zip")]
    [InlineData("C:\\evil-win-x64.zip")]
    [InlineData("/evil-win-x64.zip")]
    public async Task ReleaseWithUnsafePackageNameNeverProducesDownloadCandidate(string assetName)
    {
        var previous = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, null);
            var requests = 0;
            using var handler = new AssetNameHandler(request =>
            {
                Interlocked.Increment(ref requests);
                request.RequestUri!.AbsoluteUri.Should().Be(UpdateService.DefaultFeedUrl);
                var json = JsonSerializer.Serialize(new
                {
                    tag_name = "v99.0.0", body = "",
                    assets = new[] { new { name = assetName, browser_download_url = "https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v99.0.0/package-win-x64.zip" } }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            });
            using var http = new HttpClient(handler);
            var service = new UpdateService(http);
            var before = Directory.GetDirectories(Path.GetTempPath(), "HongxueAssistant-Update-*");
            (await service.CheckForUpdateAsync()).Should().BeNull();
            service.LastError.Should().Contain("文件名无效");
            requests.Should().Be(1, "only the release metadata may be requested");
            Directory.GetDirectories(Path.GetTempPath(), "HongxueAssistant-Update-*").Should().BeEquivalentTo(before);
            service.PendingScriptPath.Should().BeNull();
        }
        finally { Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, previous); }
    }

    [Fact]
    public void ParseChecksum_HandlesBothFormatsAndMissingEntries()
    {
        var hash = new string('a', 64);
        var other = new string('b', 64);
        var text = $"{hash}  HongxueAssistant-v1.1.2-win-x64.zip\r\n{other} *other.zip\r\n";

        UpdateService.ParseChecksum(text, "HongxueAssistant-v1.1.2-win-x64.zip").Should().Be(hash);
        UpdateService.ParseChecksum(text, "other.zip").Should().Be(other);
        UpdateService.ParseChecksum(text, "missing.zip").Should().BeNull();
        UpdateService.ParseChecksum("", "x.zip").Should().BeNull();
        UpdateService.ParseChecksum("not a checksum file", "x.zip").Should().BeNull();
    }

    [Fact]
    public void ChecksumMatches_IsCaseInsensitiveAndTrimmed()
    {
        var hash = new string('a', 64);
        UpdateService.ChecksumMatches(hash, hash.ToUpperInvariant()).Should().BeTrue();
        UpdateService.ChecksumMatches($" {hash} ", hash).Should().BeTrue();
        UpdateService.ChecksumMatches(hash, new string('c', 64)).Should().BeFalse();
    }

    [Fact]
    public async Task ComputeSha256_MatchesKnownDigest()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllTextAsync(path, "abc");
        try
        {
            // SHA-256("abc")
            (await UpdateService.ComputeSha256Async(path))
                .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DescribeError_TranslatesKnownFailures()
    {
        UpdateService.DescribeError(new UpdateIntegrityException("安装包校验不通过")).Should().Be("安装包校验不通过");
        UpdateService.DescribeError(new TaskCanceledException()).Should().Be("连接更新源超时");
        UpdateService.DescribeError(new HttpRequestException("x")).Should().Be("无法连接更新源（网络受限或代理拦截）");
        UpdateService.DescribeError(
                new HttpRequestException("x", null, System.Net.HttpStatusCode.Forbidden))
            .Should().Be("更新源拒绝了请求（可能是 GitHub 限流）");
        UpdateService.DescribeError(
                new HttpRequestException("x", null, System.Net.HttpStatusCode.NotFound))
            .Should().Contain("404");
    }

    [Fact]
    public void UpdateScript_StagesRenamesAndRollsBack()
    {
        var script = UpdateService.BuildUpdateScript(
            pid: 4321,
            sourceDir: @"C:\tmp\extracted\win-x64",
            appDir: @"D:\apps\HongxueAssistant",
            exePath: @"D:\apps\HongxueAssistant\HongxueAssistant.exe",
            stagingDir: @"D:\apps\HongxueAssistant-Staging-20261109-201500",
            backupDir: @"D:\apps\HongxueAssistant-backup-20261109-201500",
            logPath: @"C:\tmp\update.log");

        script.Should().Contain("$appPid = 4321");
        // 等待旧进程退出
        script.Should().Contain("Get-Process -Id $appPid");
        script.Should().Contain("$timeout = 300");
        // staging 与安装目录同盘（都在父目录下）
        script.Should().Contain("'D:\\apps\\HongxueAssistant-Staging-20261109-201500'");
        script.Should().Contain("'D:\\apps\\HongxueAssistant-backup-20261109-201500'");
        // 先复制到 staging，再改名切换
        script.IndexOf("Copy-Item -LiteralPath $sourceDir", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("Rename-Item -LiteralPath $appDir", StringComparison.Ordinal));
        // 切换失败要回滚并重启旧版
        script.Should().Contain("Rename-Item -LiteralPath $backupDir -NewName");
        script.Should().Contain("Start-OldVersion");
        // 成功后启动新版并清理备份
        script.Should().Contain("Start-Version $exePath");
        script.Should().Contain("Remove-Item -LiteralPath $backupDir");
        // 日志与失败提示
        script.Should().Contain("'C:\\tmp\\update.log'");
        script.Should().Contain("HongxueAssistant更新失败");
        // 无界面模式（自动化验证用）不弹框
        script.Should().Contain("$silent = ($env:HONGXUEASSISTANT_UPDATE_SILENT -eq '1')");
        // 包不完整时拒绝安装
        script.Should().Contain("更新包不完整");
    }

    [Fact]
    public void UpdateScript_RetriesRenameAndPreservesOldFilesWhenLocked()
    {
        var script = UpdateService.BuildUpdateScript(
            1, @"C:\tmp\src", @"D:\apps\app", @"D:\apps\app\HongxueAssistant.exe",
            @"D:\apps\staging", @"D:\apps\backup", @"C:\tmp\log.txt");

        // 改名最多重试 10 次，每次 500ms
        script.Should().Contain("for ($try = 1; $try -le 10; $try++)");
        script.Should().Contain("rename attempt $try failed");
        // 不逐文件覆盖：某个 DLL 被占用时不能留下半新半旧的安装目录。
        script.Should().NotContain("falling back to file overlay");
        script.Should().NotContain("Copy-Item -LiteralPath $_.FullName -Destination $target -Force");
        script.Should().Contain("未替换任何旧版文件");
        script.Should().Contain("请完全退出辅助（含托盘图标）后重新检查更新");
    }

    [Fact]
    public void DescribeError_ExplainsFileLockFromLeftoverUpdate()
    {
        var error = new IOException(
            "The process cannot access the file 'C:\\tmp\\update.ps1' because it is being used by another process.");
        UpdateService.DescribeError(error).Should().Contain("文件正被其他程序占用");

        var other = new IOException("磁盘空间不足");
        UpdateService.DescribeError(other).Should().StartWith("读写更新文件失败");
    }

    [Fact]
    public void UpdateScript_EscapesSingleQuotesInPaths()
    {
        var script = UpdateService.BuildUpdateScript(
            1, @"C:\o'neil\src", @"C:\o'neil\app", @"C:\o'neil\app\HongxueAssistant.exe",
            @"C:\o'neil\staging", @"C:\o'neil\backup", @"C:\o'neil\log.txt");

        script.Should().Contain("'C:\\o''neil\\app'");
    }
    private sealed class AssetNameHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

}
