// Modified for HongxueAssistant on 2026-11-16; see NOTICE.
// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using HongxueAssistant.Core.Interop;
using HongxueAssistant.Core.Models;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace HongxueAssistant.Core.Services;

/// <summary>Shares the selected screenshot source and desktop viewport with all consumers.</summary>
public sealed class GameConnectionService : IDisposable
{
    private readonly object _captureLock = new();
    private readonly object _windowLock = new();
    private static long _nextRevision;
    private long _captureRevision = Interlocked.Increment(ref _nextRevision);
    private MaaAdbController? _controller;
    private readonly IAdbGameScreenSource? _adbSource;
    private DateTime _retryAfter;
    private IntPtr _window;
    private uint _windowProcessId;
    private bool _disposed;
    private string _status = "PC 窗口截图";
    private int _screenWidth;
    private int _screenHeight;
    private string _windowTitle;
    private IntPtr _lastForeground;
    private int _refreshDisplay;
    private DateTime _nextDisplayRead;
    private AdbGameDisplay? _display;

    public long CaptureRevision => Volatile.Read(ref _captureRevision);
    public AdbGameDisplay? CurrentDisplay => _display;

    public GameConnectionSettings Settings { get; private set; }
    public bool IsAdb => Settings.Source == GameCaptureSource.Adb;
    public string Status => Volatile.Read(ref _status);

    public GameConnectionService(GameConnectionSettings? settings = null, IntPtr selectedWindow = default,
        IAdbGameScreenSource? adbSource = null)
    {
        Settings = settings ?? new();
        _adbSource = adbSource;
        _windowTitle = Settings.WindowTitle;
        _window = selectedWindow;
        if (_window != IntPtr.Zero)
        {
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
            _windowTitle = User32.GetWindowTitle(_window);
        }
        if (IsAdb) _status = "等待 ADB 连接";
    }

    public IntPtr FindGameWindow()
    {
        if (!IsAdb) return User32.FindArknightsWindow();
        lock (_windowLock)
        {
            var foreground = User32.GetForegroundWindow();
            if (Settings.FollowFocusedGame && foreground != _lastForeground)
            {
                _lastForeground = foreground;
                Interlocked.Exchange(ref _refreshDisplay, 1);
            }
            if (_window != IntPtr.Zero && User32.IsWindow(_window))
            {
                User32.GetWindowThreadProcessId(_window, out var processId);
                if (processId == _windowProcessId && User32.GetWindowTitle(_window) == _windowTitle)
                    return _window;
            }
            // A closed instance must not silently attach to another one with the same title.
            if (_window != IntPtr.Zero) return IntPtr.Zero;
            var matches = FindGameWindows().Where(w => w.Name == Settings.WindowTitle
                && (Settings.Emulator == EmulatorKind.Unknown || GetWindowKind(w.Handle) == Settings.Emulator)).ToList();
            if (matches.Count != 1) return IntPtr.Zero;
            _window = matches[0].Handle;
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
            return _window;
        }
    }

    public static IReadOnlyList<DesktopWindowInfo> FindGameWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && GetWindowKind(w.Handle) != EmulatorKind.Unknown)
            .Select(w => new DesktopWindowInfo(w.Handle, w.Name, w.ClassName,
                w.ScreencapMethod, w.MouseMethod, w.KeyboardMethod)).ToList();
    }

    public static EmulatorKind GetWindowKind(IntPtr window)
    {
        var display = User32.FindChildWindow(window, "nemudisplay");
        if (display != IntPtr.Zero && User32.GetAncestor(display, 2) == window) return EmulatorKind.MuMu;
        display = User32.FindChildWindow(window, "TheRender");
        return display != IntPtr.Zero && User32.GetAncestor(display, 2) == window ? EmulatorKind.LDPlayer : EmulatorKind.Unknown;
    }

    public RECT? GetViewport(IntPtr window)
    {
        if (window == IntPtr.Zero || User32.IsIconic(window)) return null;
        if (!IsAdb) return User32.GetClientRectScreen(window);
        var display = User32.FindChildWindow(window, "nemudisplay");
        if (display == IntPtr.Zero) display = User32.FindChildWindow(window, "TheRender");
        var bounds = display == IntPtr.Zero ? null : User32.GetClientRectScreen(display);
        return bounds is { Width: > 0, Height: > 0 } rect
            ? WindowGeometry.FitAspectRatio(rect, 16, 9) : null;
    }

    /// <summary>Captures the selected ADB device; its dimensions also bound information-panel scrolling.</summary>
    public byte[]? CaptureAdbScreen(int maxWidth)
    {
        lock (_captureLock)
        {
            if (_disposed || !IsAdb || DateTime.UtcNow < _retryAfter) return null;
            try
            {
                if (_controller is null)
                {
                    if (!File.Exists(Settings.AdbPath))
                        throw new IOException("请选择有效的 adb.exe 路径");
                    if (string.IsNullOrWhiteSpace(Settings.AdbSerial))
                        throw new IOException("请填写 ADB 连接地址或选择发现的设备");
                }
                var refreshDisplay = Interlocked.Exchange(ref _refreshDisplay, 0) != 0;
                if (_display is null || DateTime.UtcNow >= _nextDisplayRead || refreshDisplay)
                {
                    var display = AdbGameScreens.Select(FindDisplays(), Settings);
                    if (_display is null || _display.DisplayId != display.DisplayId || _display.PackageName != display.PackageName
                        || _display.PhysicalDisplayId != display.PhysicalDisplayId)
                    {
                        _controller?.Dispose();
                        _controller = null;
                        _display = display;
                        Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
                    }
                    _display = display;
                    Settings = Settings with { AndroidPackageName = display.PackageName, AndroidDisplayId = display.DisplayId };
                    if (Settings.FollowFocusedGame && display.IsFocused)
                    {
                        lock (_windowLock)
                        {
                            var foreground = User32.GetForegroundWindow();
                            User32.GetWindowThreadProcessId(foreground, out var processId);
                            if (processId == _windowProcessId && GetWindowKind(foreground) != EmulatorKind.Unknown)
                            {
                                if (_window != foreground)
                                    Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
                                _window = foreground;
                                _windowTitle = User32.GetWindowTitle(foreground);
                                Settings = Settings with { WindowTitle = _windowTitle };
                            }
                        }
                    }
                    _nextDisplayRead = DateTime.UtcNow.AddSeconds(1);
                }
                if (_adbSource is null && _controller is null)
                {
                    _status = "正在连接游戏屏幕…";
                    _controller = new MaaAdbController(Settings.AdbPath, Settings.AdbSerial,
                        AdbScreencapMethods.Encode, AdbInputMethods.AdbShell, AdbGameScreens.ControllerConfig(_display!),
                        agentPath: Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"), link: LinkOption.None);
                    if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
                        throw new IOException("ADB 连接失败，请检查设备与连接地址");
                    _controller.SetOption_ScreenshotUseRawSize(true);
                }

                using var image = new MaaImageBuffer();
                if (_adbSource is not null)
                {
                    if (!image.TrySetEncodedData(_adbSource.Capture(Settings.AdbPath, Settings.AdbSerial, _display!)))
                        throw new IOException("ADB 未返回有效画面");
                }
                else
                {
                    if (_controller!.Screencap().Wait() != MaaJobStatus.Succeeded)
                        throw new IOException("ADB 截图失败，请检查模拟器与连接地址");
                    if (!_controller.GetCachedImage(image) || image.IsEmpty)
                        throw new IOException("ADB 未返回有效画面");
                }
                var width = image.Width;
                var height = image.Height;
                _screenWidth = width;
                _screenHeight = height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    _status = $"当前画面 {width}×{height}，请设置为横屏 16∶9";
                    return null;
                }
                if (width > maxWidth)
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                if (!image.TryGetEncodedData(out byte[]? png) || png is null)
                    throw new IOException("ADB 画面编码失败");
                _status = $"ADB 已连接 · {_display!.Label} · {width}×{height}";
                _retryAfter = default;
                return png;
            }
            catch (Exception ex)
            {
                _status = $"{ex.Message}（5 秒后重试）";
                AppLogger.Warn("AdbCapture", _status);
                _controller?.Dispose();
                _controller = null;
                if (_display is not null)
                {
                    _display = null;
                    Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
                }
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            _disposed = true;
            _controller?.Dispose();
            _controller = null;
        }
    }

    private IReadOnlyList<AdbGameDisplay> FindDisplays()
        => _adbSource?.FindDisplays(Settings.AdbPath, Settings.AdbSerial) ?? AdbGameScreens.Find(Settings);

    /// <summary>Reject an OCR result if its game target changed after the frame was captured.</summary>
    public long RefreshCaptureRevision()
    {
        lock (_captureLock)
        {
            if (_disposed || !IsAdb || _display is null) return CaptureRevision;
            if (DateTime.UtcNow < _nextDisplayRead && Volatile.Read(ref _refreshDisplay) == 0) return CaptureRevision;
            try
            {
                var current = AdbGameScreens.Select(FindDisplays(), Settings);
                if (!SameTarget(_display, current))
                {
                    Interlocked.Exchange(ref _refreshDisplay, 1);
                    Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
                }
                _nextDisplayRead = DateTime.UtcNow.AddSeconds(1);
            }
            catch (IOException ex)
            {
                AppLogger.Warn("AdbCapture", ex.Message);
                Interlocked.Exchange(ref _refreshDisplay, 1);
                Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
            }
            return CaptureRevision;
        }
    }

    private static bool SameTarget(AdbGameDisplay? left, AdbGameDisplay right)
        => left is not null && left.DisplayId == right.DisplayId && left.PackageName == right.PackageName
            && left.PhysicalDisplayId == right.PhysicalDisplayId;

    public bool ScrollInformationPanelDown()
    {
        lock (_captureLock)
        {
            if (_disposed || _display is null || _screenWidth <= 0 || _screenHeight <= 0
                || _adbSource is null && _controller is null) return false;
            try
            {
                var display = AdbGameScreens.Select(FindDisplays(), Settings);
                if (!SameTarget(_display, display))
                {
                    Interlocked.Exchange(ref _refreshDisplay, 1);
                    // Invalidate results immediately, before the next screenshot creates a controller.
                    Interlocked.Exchange(ref _captureRevision, Interlocked.Increment(ref _nextRevision));
                    return false;
                }
                if (_adbSource is not null)
                    return _adbSource.Swipe(Settings.AdbPath, Settings.AdbSerial, display, _screenWidth, _screenHeight);
                return _controller!.Swipe((int)(_screenWidth * .35), (int)(_screenHeight * .72),
                    (int)(_screenWidth * .35), (int)(_screenHeight * .64), 200).Wait() == MaaJobStatus.Succeeded;
            }
            catch (IOException ex)
            {
                AppLogger.Warn("AdbScroll", ex.Message);
                return false;
            }
        }
    }
}
