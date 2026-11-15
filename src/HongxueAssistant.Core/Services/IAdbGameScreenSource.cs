using HongxueAssistant.Core.Models;

namespace HongxueAssistant.Core.Services;

/// <summary>Optional capture adapter for isolated device tests; production uses the Maa ADB controller.</summary>
public interface IAdbGameScreenSource
{
    IReadOnlyList<AdbGameDisplay> FindDisplays(string adbPath, string serial);
    byte[] Capture(string adbPath, string serial, AdbGameDisplay display);
    bool Swipe(string adbPath, string serial, AdbGameDisplay display, int width, int height) => false;
}
