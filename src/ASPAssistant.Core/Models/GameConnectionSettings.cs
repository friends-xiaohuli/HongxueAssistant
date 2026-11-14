// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<GameCaptureSource>))]
public enum GameCaptureSource { Pc, Adb }

public sealed record GameConnectionSettings
{
    public GameCaptureSource Source { get; init; }
    public string AdbPath { get; init; } = "";
    public string AdbSerial { get; init; } = "";
    public string AndroidPackageName { get; init; } = "";
    public int? AndroidDisplayId { get; init; }
    public bool FollowFocusedGame { get; init; } = true;
    public string WindowTitle { get; init; } = "MuMu安卓设备";
}

/// <summary>The physical screenshot target belonging to a running game activity.</summary>
public sealed record AdbGameDisplay(int DisplayId, string? PhysicalDisplayId, string PackageName, bool IsFocused = false)
{
    public string Label => $"{(PackageName == "com.hypergryph.arknights.bilibili" ? "明日方舟 B服" : "明日方舟 官服")} · 屏幕 {DisplayId}{(IsFocused ? "（当前操作）" : "")}";
}
