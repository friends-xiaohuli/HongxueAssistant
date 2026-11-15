using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using HongxueAssistant.Core.Models;
using MaaFramework.Binding;

namespace HongxueAssistant.Core.Services;

public sealed record TcpListenerOwner(string Address, int Port, int ProcessId);

public static class EmulatorDiscovery
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    public static IReadOnlyList<AdbDeviceInfo> FindDevices(string customAdbPath)
    {
        using var found = MaaToolkit.Shared.AdbDevice.Find();
        var devices = found.Select(Copy).ToList();
        if (File.Exists(customAdbPath) && devices.All(d => !SamePath(d.AdbPath, customAdbPath)))
        {
            using var custom = MaaToolkit.Shared.AdbDevice.Find(customAdbPath);
            devices.AddRange(custom.Select(Copy));
        }
        var listeners = ReadTcpListeners();
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            using var config = JsonDocument.Parse(device.Config);
            if (!config.RootElement.TryGetProperty("extras", out var extras) || !extras.TryGetProperty("ld", out var ld)
                || !ld.TryGetProperty("pid", out var process)) continue;
            var serial = ResolveLdAddress(device.AdbSerial, process.GetInt32(), listeners);
            if (serial == device.AdbSerial) continue;
            AdbGameScreens.Run(device.AdbPath, ["connect", serial]);
            devices[i] = new(device.Name, device.AdbPath, serial, device.ScreencapMethods,
                device.InputMethods, device.Config, device.AgentPath);
        }
        return devices.DistinctBy(d => d.AdbSerial, StringComparer.Ordinal).ToList();
    }

    private static AdbDeviceInfo Copy(AdbDeviceInfo device)
        => new(device.Name, device.AdbPath, device.AdbSerial, device.ScreencapMethods, device.InputMethods, device.Config, device.AgentPath);

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public static EmulatorKind Kind(string name, string adbPath)
        => name.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase) || adbPath.Contains("leidian", StringComparison.OrdinalIgnoreCase)
            || adbPath.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase) ? EmulatorKind.LDPlayer
            : name.Contains("MuMu", StringComparison.OrdinalIgnoreCase) || adbPath.Contains("MuMu", StringComparison.OrdinalIgnoreCase)
                ? EmulatorKind.MuMu : EmulatorKind.Unknown;

    public static string ResolveLdAddress(string serial, int processId, IReadOnlyList<TcpListenerOwner> listeners)
    {
        if (!serial.StartsWith("emulator-", StringComparison.Ordinal) || !int.TryParse(serial[9..], out var consolePort)) return serial;
        var port = consolePort + 1;
        // MuMu's specific loopback binding wins over LDPlayer's wildcard binding on the same port.
        if (listeners.Any(l => l.Port == port && l.Address == "0.0.0.0" && l.ProcessId == processId)
            && listeners.Any(l => l.Port == port && l.Address == "127.0.0.1" && l.ProcessId != processId)
            && !listeners.Any(l => l.Port == port && l.Address == "127.0.0.2"))
            return $"127.0.0.2:{port}";
        return serial;
    }

    private static IReadOnlyList<TcpListenerOwner> ReadTcpListeners()
    {
        var size = 0;
        var error = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        while (error == 122)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                error = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                if (error == 122) continue; // Listener rows may change between the size query and the copy.
                if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
                var result = new List<TcpListenerOwner>();
                for (var i = 0; i < Marshal.ReadInt32(buffer); i++)
                {
                    var row = buffer + 4 + i * 24;
                    var address = new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 4))).ToString();
                    var port = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(row, 8));
                    result.Add(new(address, port, Marshal.ReadInt32(row, 20)));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
        return [];
    }
}
