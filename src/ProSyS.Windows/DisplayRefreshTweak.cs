using System.Globalization;
using System.Runtime.InteropServices;
using ProSyS.Core;

namespace ProSyS.Windows;

/// <summary>
/// Sets every attached display to the highest refresh rate it supports at its current resolution and colour depth
/// (a common problem: a 144/165 Hz monitor left at 60 Hz). No administrator rights are needed; the original rates are restored exactly.
/// The captured value is a canonical "\\.\DISPLAY1=60;\\.\DISPLAY2=144;" string.
/// </summary>
public sealed class DisplayRefreshTweak : ITweak
{
    public const string BackupKind = "DisplayRefresh";
    public TweakMetadata Metadata { get; }
    public DisplayRefreshTweak(TweakMetadata metadata) => Metadata = metadata;

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        var displays = ReadDisplays();
        if (displays.Count == 0) return Task.FromResult(Result(DetectionStatus.Unavailable, null, "No attached display could be read."));
        var below = displays.Where(x => x.Current < x.Maximum).ToList();
        var detail = below.Count == 0 ? "Every display already runs at its highest refresh rate."
            : string.Join(", ", below.Select(x => $"{x.Device}: {x.Current} Hz → {x.Maximum} Hz available"));
        return Task.FromResult(Result(below.Count == 0 ? DetectionStatus.Compliant : DetectionStatus.NonCompliant, Format(displays.ToDictionary(x => x.Device, x => x.Current)), detail));
    }

    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadDisplays().Count > 0
            ? new CompatibilityResult(CompatibilityStatus.Compatible, "Display modes can be read.")
            : new CompatibilityResult(CompatibilityStatus.Unsupported, "No attached display could be read."));

    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new TweakBackup(Metadata.Id, true, Format(ReadDisplays().ToDictionary(x => x.Device, x => x.Current)), BackupKind, DateTimeOffset.UtcNow));

    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        foreach (var display in ReadDisplays().Where(x => x.Current < x.Maximum)) SetFrequency(display.Device, display.Maximum);
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);

    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        var current = ReadDisplays().ToDictionary(x => x.Device, x => x.Current, StringComparer.OrdinalIgnoreCase);
        foreach (var (device, frequency) in Parse(ValueText.Canonical(backup.OriginalValue)))
            if (current.TryGetValue(device, out var now) && now != frequency) SetFrequency(device, frequency);
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        // Only displays captured in the backup are compared; a monitor unplugged since then cannot be restored and shows as a mismatch.
        var captured = Parse(ValueText.Canonical(backup.OriginalValue)).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = ReadDisplays().Where(x => captured.Contains(x.Device)).ToDictionary(x => x.Device, x => x.Current);
        return Task.FromResult(Result(DetectionStatus.Present, Format(current), null));
    }

    public static string Format(IReadOnlyDictionary<string, int> rates) =>
        string.Concat(rates.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value.ToString(CultureInfo.InvariantCulture)};"));

    public static Dictionary<string, int> Parse(string? text) =>
        (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToDictionary(pair => pair[0], pair => int.Parse(pair[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);

    private DetectionResult Result(DetectionStatus status, object? value, string? detail) =>
        new(status, value, "Display settings (user32)", Confidence.Verified, DateTimeOffset.UtcNow, detail);

    private sealed record DisplayState(string Device, int Current, int Maximum);

    private static List<DisplayState> ReadDisplays()
    {
        var result = new List<DisplayState>();
        if (!OperatingSystem.IsWindows()) return result;
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        for (uint index = 0; EnumDisplayDevices(null, index, ref device, 0); index++, device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() })
        {
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            var current = NewMode();
            if (!EnumDisplaySettings(device.DeviceName, CurrentSettings, ref current)) continue;
            var maximum = current.DisplayFrequency;
            var mode = NewMode();
            for (var modeIndex = 0; EnumDisplaySettings(device.DeviceName, modeIndex, ref mode); modeIndex++, mode = NewMode())
                if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel
                    && (mode.DisplayFlags & Interlaced) == 0 && mode.DisplayFrequency > maximum)
                    maximum = mode.DisplayFrequency;
            // 0 and 1 mean "hardware default"; there is nothing meaningful to compare.
            if (current.DisplayFrequency > 1) result.Add(new(device.DeviceName, current.DisplayFrequency, maximum));
        }
        return result;
    }

    private static void SetFrequency(string device, int frequency)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(device, CurrentSettings, ref mode)) throw new InvalidOperationException($"The current mode of {device} could not be read.");
        mode.DisplayFrequency = frequency;
        mode.Fields = DisplayFrequencyField;
        if (ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, Test, IntPtr.Zero) != 0) throw new InvalidOperationException($"{device} does not accept {frequency} Hz at its current resolution.");
        var result = ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, UpdateRegistry, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException($"Windows refused to set {device} to {frequency} Hz (code {result}).");
    }

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    private const int CurrentSettings = -1, AttachedToDesktop = 0x1, Interlaced = 0x2, DisplayFrequencyField = 0x400000;
    private const uint UpdateRegistry = 0x1, Test = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int modeNumber, ref DevMode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref DevMode mode, IntPtr window, uint flags, IntPtr parameter);
}
