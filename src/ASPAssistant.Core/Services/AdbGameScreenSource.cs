// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public interface IAdbGameScreenSource
{
    IReadOnlyList<AdbGameDisplay> FindDisplays(string adbPath, string serial);
    byte[] Capture(string adbPath, string serial, AdbGameDisplay display);
    bool Swipe(string adbPath, string serial, AdbGameDisplay display, int width, int height) => false;
}

/// <summary>Display discovery and capture use read-only ADB commands; scrolling is explicitly targeted to the same logical game display.</summary>
public sealed class AdbGameScreenSource : IAdbGameScreenSource
{
    public IReadOnlyList<AdbGameDisplay> FindDisplays(string adbPath, string serial)
    {
        if (serial.Contains(':')) Run(adbPath, serial, "connect", serial);
        var activities = Encoding.UTF8.GetString(Run(adbPath, serial, "shell", "dumpsys", "activity", "activities"));
        var displays = Encoding.UTF8.GetString(Run(adbPath, serial, "shell", "dumpsys", "display"));
        var input = Encoding.UTF8.GetString(Run(adbPath, serial, "shell", "dumpsys", "input"));
        return ParseDisplays(activities, displays, input);
    }

    public byte[] Capture(string adbPath, string serial, AdbGameDisplay display)
    {
        // Android screencap uses the physical id, not dumpsys activity's logical display id.
        if (display.PhysicalDisplayId is { } physicalId)
            return Run(adbPath, serial, "exec-out", "screencap", "-p", "-d", physicalId);
        if (display.DisplayId == 0)
            return Run(adbPath, serial, "exec-out", "screencap", "-p");
        throw new IOException("无法确定游戏屏幕的截图编号，请刷新游戏屏幕列表。");
    }

    public bool Swipe(string adbPath, string serial, AdbGameDisplay display, int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        Run(adbPath, serial, "shell", "input", "-d", display.DisplayId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "swipe", ((int)(width * .35)).ToString(), ((int)(height * .72)).ToString(),
            ((int)(width * .35)).ToString(), ((int)(height * .64)).ToString(), "200");
        return true;
    }

    public static IReadOnlyList<AdbGameDisplay> ParseDisplays(string activities, string displays, string input = "")
    {
        var physicalIds = new Dictionary<int, string>();
        var blocks = Regex.Matches(displays, @"(?m)^\s*Display (\d+):\s*\r?$");
        for (var i = 0; i < blocks.Count; i++)
        {
            var start = blocks[i].Index;
            var end = i + 1 < blocks.Count ? blocks[i + 1].Index : displays.Length;
            var block = displays[start..end];
            var id = Regex.Match(block, "uniqueId[=\\s]+\\\"?local:(\\d+)");
            if (id.Success)
                physicalIds[int.Parse(blocks[i].Groups[1].Value)] = id.Groups[1].Value;
        }

        var result = new List<AdbGameDisplay>();
        int? displayId = null;
        foreach (var line in activities.Split('\n'))
        {
            var display = Regex.Match(line, @"^\s*Display #(\d+)\b");
            if (display.Success) displayId = int.Parse(display.Groups[1].Value);
            var activity = Regex.Match(line, @"(?:topResumedActivity|mResumedActivity)\s*=.*?\bu\d+\s+([\w.]+)/");
            if (!activity.Success) continue;
            var package = activity.Groups[1].Value;
            if (package is not ("com.hypergryph.arknights" or "com.hypergryph.arknights.bilibili")) continue;
            var logicalId = displayId ?? 0;
            physicalIds.TryGetValue(logicalId, out var physicalId);
            if (logicalId != 0 && physicalId is null) continue;
            var item = new AdbGameDisplay(logicalId, physicalId, package);
            if (!result.Contains(item)) result.Add(item);
        }
        var focus = Regex.Match(input, @"FocusedDisplayId\s*:\s*(\d+)");
        int? focusedId = focus.Success ? int.Parse(focus.Groups[1].Value) : null;
        if (focusedId is null)
        {
            var resumed = Regex.Match(activities, @"(?m)^\s*ResumedActivity:.*?\bu\d+\s+([\w.]+)/");
            var resumedDisplays = result.Where(d => resumed.Success && d.PackageName == resumed.Groups[1].Value).ToList();
            if (resumedDisplays.Count == 1) focusedId = resumedDisplays[0].DisplayId;
        }
        return result.OrderBy(d => d.DisplayId).Select(d => d with { IsFocused = d.DisplayId == focusedId }).ToList();
    }

    public static AdbGameDisplay SelectDisplay(IReadOnlyList<AdbGameDisplay> displays, GameConnectionSettings settings)
    {
        if (settings.FollowFocusedGame)
        {
            var focused = displays.Where(d => d.IsFocused).ToList();
            if (focused.Count == 1) return focused[0];
        }
        var matches = displays.Where(d => string.IsNullOrEmpty(settings.AndroidPackageName)
            || d.PackageName == settings.AndroidPackageName).ToList();
        // Package identity survives a MuMu restart even if logical/physical display ids change.
        if (matches.Count > 1 && settings.AndroidDisplayId is { } id)
            matches = matches.Where(d => d.DisplayId == id).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new IOException("未找到所选明日方舟游戏屏幕，请打开游戏并在“游戏连接”中刷新屏幕。"),
            _ => throw new IOException("发现多个明日方舟游戏屏幕，请在“游戏连接”中选择官服或 B服屏幕。"),
        };
    }

    private static byte[] Run(string adbPath, string serial, params string[] arguments)
        => RunAsync(adbPath, serial, arguments).GetAwaiter().GetResult();

    private static async Task<byte[]> RunAsync(string adbPath, string serial, string[] arguments)
    {
        var start = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(serial);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 ADB。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(copy, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            throw new IOException("ADB 读取超时，请检查模拟器状态。");
        }
        if (process.ExitCode != 0)
            throw new IOException($"ADB 读取失败：{(await error).Trim()}");
        return output.ToArray();
    }
}
