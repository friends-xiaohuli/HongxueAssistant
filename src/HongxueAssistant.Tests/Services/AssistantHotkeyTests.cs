using System.Text.Json;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

public class AssistantHotkeyTests
{
    [Theory]
    [InlineData(0, 65)]
    [InlineData(8, 65)]
    [InlineData(2, 123)]
    [InlineData(1, 9)]
    [InlineData(1, 115)]
    [InlineData(3, 46)]
    [InlineData(6, 27)]
    [InlineData(1, 44)]
    [InlineData(2, 17)]
    [InlineData(32, 65)]
    [InlineData(7, 255)]
    public void ReservedOrInvalidShortcutExplainsConflict(uint modifiers, uint key)
        => new AssistantHotkey(modifiers, key).ConflictReason().Should().NotBeNullOrWhiteSpace();

    [Theory]
    [InlineData(6, 0xBF)]
    [InlineData(6, 0x6F)]
    public void BothSlashKeysAreValidForConfigurableShortcuts(uint modifiers, uint key)
        => new AssistantHotkey(modifiers, key).ConflictReason().Should().BeNull();

    [Fact]
    public async Task RecognitionDefaultCanBeChangedClearedAndReloadedIndependently()
    {
        var folder = Path.Combine(Path.GetTempPath(), "recognition-hotkey-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsManager(folder);
            (await settings.LoadRecognitionHotkeyAsync()).Should().Be(new AssistantHotkey(6, 0xBF));
            await settings.SaveAssistantHotkeyAsync(new AssistantHotkey(7, 72));
            var chosen = new AssistantHotkey(6, 0x6F);
            await settings.SaveRecognitionHotkeyAsync(chosen);
            (await new SettingsManager(folder).LoadRecognitionHotkeyAsync()).Should().Be(chosen);
            await settings.SaveRecognitionHotkeyAsync(null);
            (await new SettingsManager(folder).LoadRecognitionHotkeyAsync()).Should().BeNull();
            (await settings.LoadAssistantHotkeyAsync()).Should().Be(new AssistantHotkey(7, 72));
            JsonSerializer.Deserialize<AssistantHotkey>(await File.ReadAllTextAsync(Path.Combine(folder, "recognition-hotkey.json.bak")))
                .Should().Be(chosen);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Modifiers\":8,\"VirtualKey\":65}")]
    public async Task InvalidRecognitionConfigurationIsReported(string contents)
    {
        var folder = Path.Combine(Path.GetTempPath(), "recognition-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "recognition-hotkey.json"), contents);
            var load = () => new SettingsManager(folder).LoadRecognitionHotkeyAsync();
            await load.Should().ThrowAsync<JsonException>();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task SelectionPersistsClearsAndKeepsPreviousBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "assistant-hotkey-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(folder);
        try
        {
            (await settings.LoadAssistantHotkeyAsync()).Should().BeNull();
            var shortcut = new AssistantHotkey(7, 72);
            shortcut.ConflictReason().Should().BeNull();
            await settings.SaveAssistantHotkeyAsync(shortcut);
            (await new SettingsManager(folder).LoadAssistantHotkeyAsync()).Should().Be(shortcut);
            await settings.SaveAssistantHotkeyAsync(null);
            (await settings.LoadAssistantHotkeyAsync()).Should().BeNull();
            JsonSerializer.Deserialize<AssistantHotkey>(await File.ReadAllTextAsync(Path.Combine(folder, "assistant-hotkey.json.bak")))
                .Should().Be(shortcut);
            await File.WriteAllTextAsync(Path.Combine(folder, "assistant-hotkey.json"), "{\"Modifiers\":8,\"VirtualKey\":65}");
            var load = () => settings.LoadAssistantHotkeyAsync();
            await load.Should().ThrowAsync<JsonException>();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
