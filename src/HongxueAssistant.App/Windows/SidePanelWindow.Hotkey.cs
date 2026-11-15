using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using HongxueAssistant.Core;
using HongxueAssistant.Core.Interop;
using HongxueAssistant.Core.Models;

namespace HongxueAssistant.App.Windows;

public partial class SidePanelWindow
{
    private AssistantHotkey? _assistantHotkey;
    private int _assistantHotkeyId = 0x4154;
    private AssistantHotkey? _recognitionHotkey;
    private int _recognitionHotkeyId = HotkeyIdManualScan;
    private bool _savingHotkey;
    public bool IsAssistantHidden { get; internal set; }
    public event Action? ToggleAssistantVisibilityRequested;

    public Task InitAssistantHotkeyAsync() => InitHotkeyAsync(false);
    public Task InitRecognitionHotkeyAsync() => InitHotkeyAsync(true);

    private async Task InitHotkeyAsync(bool recognition)
    {
        var box = recognition ? RecognitionHotkeyBox : AssistantHotkeyBox;
        var status = recognition ? RecognitionHotkeyStatus : AssistantHotkeyStatus;
        try
        {
            var hotkey = recognition ? await _settingsManager.LoadRecognitionHotkeyAsync() : await _settingsManager.LoadAssistantHotkeyAsync();
            box.Text = hotkey is null ? "未设置（点击录入）" : HotkeyText(hotkey);
            if (recognition) RunBanView.SetRecognitionHotkeyText(null);
            if (hotkey is null) return;
            var other = recognition ? _assistantHotkey : _recognitionHotkey;
            var id = recognition ? _recognitionHotkeyId : _assistantHotkeyId;
            string? error;
            if (hotkey == other) error = recognition ? "与隐藏 / 显示助手快捷键冲突" : "与快速识别快捷键冲突";
            else if (TryRegisterHotkey(hotkey, id, out error))
            {
                if (recognition)
                {
                    _recognitionHotkey = hotkey;
                    RunBanView.SetRecognitionHotkeyText(HotkeyText(hotkey));
                }
                else _assistantHotkey = hotkey;
                status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                status.Text = "快捷键已启用";
                return;
            }
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            status.Text = $"保存的快捷键 {HotkeyText(hotkey)} 未生效：{error}";
            AppLogger.Warn("Hotkey", status.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            status.Text = $"无法读取快捷键：{ex.Message}，请重新设置";
            AppLogger.Warn("Hotkey", status.Text);
        }
    }

    private bool TryRegisterHotkey(AssistantHotkey hotkey, int id, out string? error)
    {
        error = null;
        if (User32.RegisterHotKey(new WindowInteropHelper(this).Handle, id, hotkey.Modifiers | 0x4000, hotkey.VirtualKey))
            return true;
        var code = Marshal.GetLastWin32Error();
        error = code == 1409 ? "该组合键已被系统或其他程序占用，请换一组" : $"Windows 无法注册快捷键（错误 {code}），请换一组";
        return false;
    }

    private async void OnAssistantHotkeyKeyDown(object sender, KeyEventArgs e) => await RecordHotkeyAsync(e, false);
    private async void OnRecognitionHotkeyKeyDown(object sender, KeyEventArgs e) => await RecordHotkeyAsync(e, true);

    private async Task RecordHotkeyAsync(KeyEventArgs e, bool recognition)
    {
        e.Handled = true;
        if (_savingHotkey || e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        var hotkey = new AssistantHotkey(
            (modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0),
            (uint)KeyInterop.VirtualKeyFromKey(key));
        await SaveHotkeyAsync(hotkey, recognition);
    }

    private async void OnClearAssistantHotkey(object sender, RoutedEventArgs e) => await SaveAssistantHotkeyAsync(null);
    private async void OnClearRecognitionHotkey(object sender, RoutedEventArgs e) => await SaveRecognitionHotkeyAsync(null);
    internal Task SaveAssistantHotkeyAsync(AssistantHotkey? hotkey) => SaveHotkeyAsync(hotkey, false);
    internal Task SaveRecognitionHotkeyAsync(AssistantHotkey? hotkey) => SaveHotkeyAsync(hotkey, true);

    private async Task SaveHotkeyAsync(AssistantHotkey? hotkey, bool recognition)
    {
        var current = recognition ? _recognitionHotkey : _assistantHotkey;
        if (_savingHotkey || (hotkey is not null && hotkey == current)) return;
        var status = recognition ? RecognitionHotkeyStatus : AssistantHotkeyStatus;
        var box = recognition ? RecognitionHotkeyBox : AssistantHotkeyBox;
        var other = recognition ? _assistantHotkey : _recognitionHotkey;
        var error = hotkey?.ConflictReason();
        if (hotkey is not null && hotkey == other)
            error = recognition ? "与隐藏 / 显示助手快捷键冲突" : "与快速识别快捷键冲突";
        if (error is not null)
        {
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            status.Text = error + "；原设置保留";
            return;
        }
        var currentId = recognition ? _recognitionHotkeyId : _assistantHotkeyId;
        var nextId = recognition ? (currentId == HotkeyIdManualScan ? 0x4156 : HotkeyIdManualScan)
            : (currentId == 0x4154 ? 0x4155 : 0x4154);
        if (hotkey is not null && !TryRegisterHotkey(hotkey, nextId, out error))
        {
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            status.Text = error + "；原设置保留";
            return;
        }
        _savingHotkey = true;
        AssistantHotkeyBox.IsEnabled = ClearHotkeyButton.IsEnabled = RecognitionHotkeyBox.IsEnabled = ClearRecognitionHotkeyButton.IsEnabled = false;
        try
        {
            if (recognition) await _settingsManager.SaveRecognitionHotkeyAsync(hotkey);
            else await _settingsManager.SaveAssistantHotkeyAsync(hotkey);
            if (current is not null) User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, currentId);
            if (recognition)
            {
                _recognitionHotkeyId = nextId;
                _recognitionHotkey = hotkey;
                RunBanView.SetRecognitionHotkeyText(hotkey is null ? null : HotkeyText(hotkey));
            }
            else
            {
                _assistantHotkeyId = nextId;
                _assistantHotkey = hotkey;
            }
            box.Text = hotkey is null ? "未设置（点击录入）" : HotkeyText(hotkey);
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            status.Text = hotkey is null ? "已清除快捷键" : recognition ? "已保存，按下可录入本局信息" : "已保存，按下可隐藏 / 显示助手";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (hotkey is not null) User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, nextId);
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            status.Text = $"保存失败：{ex.Message}；原设置保留";
        }
        finally
        {
            _savingHotkey = false;
            AssistantHotkeyBox.IsEnabled = ClearHotkeyButton.IsEnabled = RecognitionHotkeyBox.IsEnabled = ClearRecognitionHotkeyButton.IsEnabled = true;
        }
    }

    private static string HotkeyText(AssistantHotkey hotkey)
        => ((hotkey.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((hotkey.Modifiers & 1) != 0 ? "Alt + " : "")
            + ((hotkey.Modifiers & 4) != 0 ? "Shift + " : "") + (hotkey.VirtualKey switch
            {
                0x6F => "小键盘 ÷", 0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/",
                0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
                _ => KeyInterop.KeyFromVirtualKey((int)hotkey.VirtualKey).ToString()
            });

    public void ReleaseAssistantHotkey()
    {
        if (_assistantHotkey is not null)
            User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _assistantHotkeyId);
    }

    public void ReleaseRecognitionHotkey()
    {
        if (_recognitionHotkey is not null)
            User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _recognitionHotkeyId);
    }
}
