using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class RegistryTweak : ITweak
{
    private readonly string _subKey;
    private readonly string _valueName;
    private readonly object _recommended;
    private readonly RegistryValueKind _kind;
    public TweakMetadata Metadata { get; }

    public RegistryTweak(TweakMetadata metadata, string subKey, string valueName, object recommended, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        var allowedRoots = new[]
        {
            "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR",
            "Software\\Microsoft\\GameBar",
            "System\\GameConfigStore",
            "Control Panel\\Mouse",
            "Control Panel\\Desktop",
            "Control Panel\\Accessibility",
            "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
            "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects",
            "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
            "Software\\Microsoft\\Windows\\CurrentVersion\\Search",
            "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager",
            "Software\\Microsoft\\Windows\\DWM",
            "Software\\Microsoft\\InputPersonalization",
            "Software\\Microsoft\\TabletTip",
            "Software\\Microsoft\\Multimedia\\Audio",
            "Software\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia"
        };
        var allowed = allowedRoots.Any(root => subKey.Equals(root, StringComparison.OrdinalIgnoreCase)
            || subKey.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            throw new ArgumentException("The HKCU path is not in the gaming-settings allowlist.", nameof(subKey));
        Metadata = metadata;
        _subKey = subKey;
        _valueName = valueName;
        _recommended = recommended;
        _kind = kind;
    }

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey, false);
            var value = key?.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var compliant = value is not null && string.Equals(value.ToString(), _recommended.ToString(), StringComparison.Ordinal);
            return Task.FromResult(new DetectionResult(compliant ? DetectionStatus.Disabled : DetectionStatus.Enabled,
                value, $"HKCU\\{_subKey}\\{_valueName}", Confidence.Verified, DateTimeOffset.UtcNow,
                compliant ? "Recommended state is already applied." : "A reversible user-level change is available."));
        }
        catch (UnauthorizedAccessException ex) { return Task.FromResult(new DetectionResult(DetectionStatus.PermissionRequired, null, "Windows Registry", Confidence.Unknown, DateTimeOffset.UtcNow, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new DetectionResult(DetectionStatus.DetectionFailed, null, "Windows Registry", Confidence.Unknown, DateTimeOffset.UtcNow, ex.Message)); }
    }

    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) =>
        Task.FromResult(machine.WindowsBuild >= 22000
            ? new CompatibilityResult(CompatibilityStatus.Compatible, "Supported on detected Windows 11 build.")
            : new CompatibilityResult(CompatibilityStatus.Unsupported, "This MVP supports Windows 11 build 22000 or newer."));

    public Task<TweakBackup> BackupAsync(CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, false);
        var names = key?.GetValueNames() ?? Array.Empty<string>();
        var existed = names.Contains(_valueName, StringComparer.OrdinalIgnoreCase);
        var original = existed ? key!.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        var kind = existed ? key!.GetValueKind(_valueName).ToString() : RegistryValueKind.None.ToString();
        return Task.FromResult(new TweakBackup(Metadata.Id, existed, original, kind, DateTimeOffset.UtcNow));
    }

    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey, true);
        key.SetValue(_valueName, _recommended, _kind);
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);

    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey, true);
        if (!backup.ValueExisted) key.DeleteValue(_valueName, false);
        else key.SetValue(_valueName, ConvertBackupValue(backup), Enum.Parse<RegistryValueKind>(backup.ValueKind));
        return Task.CompletedTask;
    }

    public async Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        var result = await ReadRawAsync();
        return result;
    }

    private Task<DetectionResult> ReadRawAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, false);
        var exists = key?.GetValueNames().Contains(_valueName, StringComparer.OrdinalIgnoreCase) == true;
        var value = exists ? key!.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        return Task.FromResult(new DetectionResult(exists ? DetectionStatus.Enabled : DetectionStatus.Unknown, value,
            $"HKCU\\{_subKey}\\{_valueName}", Confidence.Verified, DateTimeOffset.UtcNow));
    }

    private static object ConvertBackupValue(TweakBackup backup)
    {
        if (backup.OriginalValue is System.Text.Json.JsonElement element)
            return backup.ValueKind switch
            {
                nameof(RegistryValueKind.DWord) => element.GetInt32(),
                nameof(RegistryValueKind.QWord) => element.GetInt64(),
                _ => element.GetString() ?? string.Empty
            };
        return backup.OriginalValue ?? string.Empty;
    }
}
