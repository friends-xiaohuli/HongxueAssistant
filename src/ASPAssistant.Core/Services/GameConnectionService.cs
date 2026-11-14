// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using System.Buffers.Binary;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>Shares the selected screenshot source and desktop viewport with all consumers.</summary>
public sealed class GameConnectionService : IDisposable
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private readonly object _captureLock = new();
    private readonly IAdbGameScreenSource _adbSource;
    private AdbGameDisplay? _gameDisplay;
    private DateTime _refreshDisplayAfter;
    private DateTime _retryAfter;
    private IntPtr _window;
    private uint _windowProcessId;
    private string _windowTitle;
    private static long _nextCaptureRevision;
    private long _captureRevision = Interlocked.Increment(ref _nextCaptureRevision);
    private int _refreshDisplayRequested;
    private int _screenWidth;
    private int _screenHeight;
    private bool _disposed;
    private string _status = "PC 窗口截图";

    public GameConnectionSettings Settings { get; private set; }
    public bool IsAdb => Settings.Source == GameCaptureSource.Adb;
    public string Status => Volatile.Read(ref _status);
    public long CaptureRevision => Interlocked.Read(ref _captureRevision);

    public GameConnectionService(GameConnectionSettings? settings = null, IntPtr selectedWindow = default,
        IAdbGameScreenSource? adbSource = null)
    {
        Settings = settings ?? new();
        _windowTitle = Settings.WindowTitle;
        _adbSource = adbSource ?? new AdbGameScreenSource();
        _window = selectedWindow;
        if (_window != IntPtr.Zero)
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
        if (IsAdb) _status = "等待 ADB 连接";
    }

    public IntPtr FindGameWindow()
    {
        if (!IsAdb) return User32.FindArknightsWindow();
        if (_window != IntPtr.Zero && User32.IsWindow(_window))
        {
            // Follow another game window only inside the already selected MuMu process.
            // Other emulator instances retain their own independently selected ADB device.
            var foreground = User32.GetForegroundWindow();
            if (Settings.FollowFocusedGame && foreground != IntPtr.Zero && foreground != _window && IsMuMuGameWindow(foreground))
            {
                User32.GetWindowThreadProcessId(foreground, out var foregroundProcess);
                if (foregroundProcess == _windowProcessId)
                {
                    _window = foreground;
                    _windowTitle = User32.GetWindowTitle(foreground);
                    Interlocked.Exchange(ref _refreshDisplayRequested, 1);
                }
            }
            User32.GetWindowThreadProcessId(_window, out var processId);
            if (processId == _windowProcessId && User32.GetWindowTitle(_window) == _windowTitle)
                return _window;
        }

        // Duplicate titles need an explicit selection in the connection dialog.
        // A closed instance must never silently attach the overlay to another instance.
        if (_window != IntPtr.Zero) return IntPtr.Zero;
        var matches = FindMuMuWindows().Where(w => w.Name == Settings.WindowTitle).ToList();
        if (matches.Count != 1) return IntPtr.Zero;
        _window = matches[0].Handle;
        User32.GetWindowThreadProcessId(_window, out _windowProcessId);
        return _window;
    }

    public static IReadOnlyList<DesktopWindowInfo> FindMuMuWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && IsMuMuGameWindow(w.Handle))
            .Select(w => new DesktopWindowInfo(w.Handle, w.Name, w.ClassName,
                w.ScreencapMethod, w.MouseMethod, w.KeyboardMethod)).ToList();
    }

    private static bool IsMuMuGameWindow(IntPtr window)
    {
        var display = User32.FindChildWindow(window, "nemudisplay");
        return display != IntPtr.Zero && User32.GetAncestor(display, 2) == window;
    }

    public RECT? GetViewport(IntPtr window)
    {
        if (window == IntPtr.Zero || User32.IsIconic(window)) return null;
        if (!IsAdb) return User32.GetClientRectScreen(window);
        var display = User32.FindChildWindow(window, "nemudisplay");
        var bounds = display == IntPtr.Zero ? null : User32.GetClientRectScreen(display);
        return bounds is { Width: > 0, Height: > 0 } rect
            ? WindowGeometry.FitAspectRatio(rect, 16, 9) : null;
    }

    /// <summary>Captures the game's own Android display, independently of the desktop viewport.</summary>
    public byte[]? CaptureAdbScreen(int maxWidth)
    {
        lock (_captureLock)
        {
            if (_disposed || !IsAdb || DateTime.UtcNow < _retryAfter) return null;
            try
            {
                if (!File.Exists(Settings.AdbPath)) throw new IOException("请选择有效的 adb.exe 路径");
                if (string.IsNullOrWhiteSpace(Settings.AdbSerial))
                    throw new IOException("请填写 ADB 连接地址或选择发现的设备");
                RefreshDisplay();
                var png = _adbSource.Capture(Settings.AdbPath, Settings.AdbSerial, _gameDisplay!);
                if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(PngSignature))
                    throw new IOException("ADB 未返回有效画面");
                var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
                var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
                _screenWidth = width;
                _screenHeight = height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    _status = $"当前画面 {width}×{height}，请设置为横屏 16∶9";
                    return null;
                }
                if (width > maxWidth)
                {
                    using var image = new MaaImageBuffer();
                    if (!image.TrySetEncodedData(png)) throw new IOException("ADB 画面解码失败");
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                    if (!image.TryGetEncodedData(out byte[]? resized) || resized is null)
                        throw new IOException("ADB 画面编码失败");
                    png = resized;
                }
                _status = $"ADB 已连接 · {_gameDisplay!.Label} · {width}×{height}";
                _retryAfter = default;
                return png;
            }
            catch (Exception ex)
            {
                _status = $"{ex.Message}（5 秒后重试）";
                AppLogger.Warn("AdbCapture", _status);
                _gameDisplay = null;
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                return null;
            }
        }
    }

    private void RefreshDisplay(bool force = false)
    {
        if (!force && _gameDisplay is not null && DateTime.UtcNow < _refreshDisplayAfter
            && Interlocked.Exchange(ref _refreshDisplayRequested, 0) == 0) return;
        var display = AdbGameScreenSource.SelectDisplay(_adbSource.FindDisplays(Settings.AdbPath, Settings.AdbSerial), Settings);
        if (_gameDisplay is null || _gameDisplay.PackageName != display.PackageName
            || _gameDisplay.DisplayId != display.DisplayId || _gameDisplay.PhysicalDisplayId != display.PhysicalDisplayId)
            Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextCaptureRevision));
        _gameDisplay = display;
        Settings = Settings with { AndroidPackageName = display.PackageName, AndroidDisplayId = display.DisplayId };
        _refreshDisplayAfter = DateTime.UtcNow.AddSeconds(1);
    }

    /// <summary>Re-check the game target before publishing work that outlived a capture.</summary>
    public long RefreshCaptureRevision()
    {
        lock (_captureLock)
        {
            if (!_disposed && IsAdb && _gameDisplay is not null)
            {
                try { RefreshDisplay(); }
                catch (IOException) { _gameDisplay = null; Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextCaptureRevision)); }
            }
            return CaptureRevision;
        }
    }

    public bool ScrollInformationPanelDown()
    {
        lock (_captureLock)
        {
            if (_disposed || _gameDisplay is null) return false;
            var previous = _gameDisplay;
            try
            {
                // A gesture must never land on another game after a focus or display change.
                RefreshDisplay(force: true);
                if (_gameDisplay!.DisplayId != previous.DisplayId || _gameDisplay.PackageName != previous.PackageName
                    || _gameDisplay.PhysicalDisplayId != previous.PhysicalDisplayId) return false;
                return _adbSource.Swipe(Settings.AdbPath, Settings.AdbSerial, _gameDisplay, _screenWidth, _screenHeight);
            }
            catch (IOException ex) { AppLogger.Warn("AdbScroll", ex.Message); return false; }
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            _disposed = true;
            _gameDisplay = null;
        }
    }
}
