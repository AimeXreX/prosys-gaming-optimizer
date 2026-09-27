using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

public sealed class RegistryTweak : ITweak
{
    private readonly string _subKey;
    private readonly string _valueName;
    private readonly object _recommended;
    private readonly RegistryValueKind _kind;
    private readonly string? _token;
    private readonly Func<MachineSnapshot, CompatibilityResult>? _compatibility;
    public TweakMetadata Metadata { get; }

    public const string DirectXUserGpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>
    /// Sets one <c>Name=Value;</c> token inside a DirectX preference string (the format Windows Settings › Display › Graphics writes),
    /// keeping every other token. Backup and rollback still restore the whole original string exactly.
    /// </summary>
    public static RegistryTweak DirectXSetting(TweakMetadata metadata, string valueName, string token, string tokenValue,
        Func<MachineSnapshot, CompatibilityResult> compatibility) =>
        new(metadata, DirectXUserGpuPreferences, valueName, tokenValue, RegistryValueKind.String, token, compatibility);

    public RegistryTweak(TweakMetadata metadata, string subKey, string valueName, object recommended, RegistryValueKind kind = RegistryValueKind.DWord)
        : this(metadata, subKey, valueName, recommended, kind, null, null) { }

    private RegistryTweak(TweakMetadata metadata, string subKey, string valueName, object recommended, RegistryValueKind kind,
        string? token, Func<MachineSnapshot, CompatibilityResult>? compatibility)
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
            "Software\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia",
            DirectXUserGpuPreferences
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
        _token = token;
        _compatibility = compatibility;
    }

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey, false);
            var value = key?.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var compliant = _token is null
                ? value is not null && string.Equals(ValueText.Canonical(value), ValueText.Canonical(_recommended), StringComparison.Ordinal)
                : value is string text && ReadToken(text, _token) == _recommended.ToString();
            return Task.FromResult(new DetectionResult(compliant ? DetectionStatus.Compliant : DetectionStatus.NonCompliant,
                value, $"HKCU\\{_subKey}\\{_valueName}", Confidence.Verified, DateTimeOffset.UtcNow,
                compliant ? "Recommended state is already applied." : "A reversible user-level change is available."));
        }
        catch (UnauthorizedAccessException ex) { return Task.FromResult(new DetectionResult(DetectionStatus.PermissionRequired, null, "Windows Registry", Confidence.Unknown, DateTimeOffset.UtcNow, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new DetectionResult(DetectionStatus.DetectionFailed, null, "Windows Registry", Confidence.Unknown, DateTimeOffset.UtcNow, ex.Message)); }
    }

    public Task<CompatibilityResult> EvaluateCompatibilityAsync(MachineSnapshot machine, CancellationToken cancellationToken = default) =>
        _compatibility is not null ? Task.FromResult(_compatibility(machine)) :
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
        return Task.FromResult(new TweakBackup(Metadata.Id, existed, original, kind, DateTimeOffset.UtcNow, key is null ? FindMissingKeyPath(_subKey) : null));
    }

    public Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subKey, true);
        var value = _token is null ? _recommended : WriteToken(key.GetValue(_valueName) as string, _token, _recommended.ToString()!);
        key.SetValue(_valueName, value, _kind);
        return Task.CompletedTask;
    }

    /// <summary>Reads <paramref name="token"/> from a <c>Name=Value;Name=Value;</c> string.</summary>
    public static string? ReadToken(string text, string token) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Equals(token, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1]).LastOrDefault();

    /// <summary>Sets <paramref name="token"/> in a <c>Name=Value;</c> string, keeping the other tokens and their order.</summary>
    public static string WriteToken(string? text, string token, string value)
    {
        var parts = (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.Split('=', 2)[0].Equals(token, StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add($"{token}={value}");
        return string.Join(";", parts) + ";";
    }

    public Task<DetectionResult> VerifyAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);

    public Task RollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        if (!backup.ValueExisted)
        {
            using (var existing = Registry.CurrentUser.OpenSubKey(_subKey, true)) existing?.DeleteValue(_valueName, false);
            RemoveCreatedKeys(backup.MissingKeyPath);
            return Task.CompletedTask;
        }
        using var key = Registry.CurrentUser.CreateSubKey(_subKey, true);
        key.SetValue(_valueName, ConvertBackupValue(backup), Enum.Parse<RegistryValueKind>(backup.ValueKind));
        return Task.CompletedTask;
    }

    public Task<DetectionResult> VerifyRollbackAsync(TweakBackup backup, CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subKey, false);
        var exists = key?.GetValueNames().Contains(_valueName, StringComparer.OrdinalIgnoreCase) == true;
        var value = exists ? key!.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        var source = $"HKCU\\{_subKey}\\{_valueName}";
        if (!exists) return Task.FromResult(new DetectionResult(DetectionStatus.Absent, null, source, Confidence.Verified, DateTimeOffset.UtcNow));
        var kind = key!.GetValueKind(_valueName).ToString();
        var sameKind = !backup.ValueExisted || kind == backup.ValueKind;
        return Task.FromResult(new DetectionResult(sameKind ? DetectionStatus.Present : DetectionStatus.NonCompliant, value, source, Confidence.Verified, DateTimeOffset.UtcNow,
            sameKind ? null : $"Registry type is {kind}; the original type was {backup.ValueKind}."));
    }

    /// <summary>Returns the highest ancestor of <paramref name="subKey"/> that does not exist yet.</summary>
    private static string? FindMissingKeyPath(string subKey)
    {
        var parts = subKey.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('\\', parts[..i]);
            using var key = Registry.CurrentUser.OpenSubKey(path, false);
            if (key is null) return path;
        }
        return null;
    }

    /// <summary>Deletes keys created by apply, deepest first, stopping at the first key that still has content.</summary>
    private void RemoveCreatedKeys(string? missingKeyPath)
    {
        if (missingKeyPath is null || !(_subKey.Equals(missingKeyPath, StringComparison.OrdinalIgnoreCase) || _subKey.StartsWith(missingKeyPath + "\\", StringComparison.OrdinalIgnoreCase))) return;
        var path = _subKey;
        while (true)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path, false))
                if (key is not null && (key.SubKeyCount > 0 || key.ValueCount > 0)) return;
            Registry.CurrentUser.DeleteSubKey(path, false);
            if (path.Length <= missingKeyPath.Length) return;
            path = path[..path.LastIndexOf('\\')];
        }
    }

    public static object ConvertBackupValue(TweakBackup backup)
    {
        var kind = Enum.Parse<RegistryValueKind>(backup.ValueKind);
        if (backup.OriginalValue is System.Text.Json.JsonElement element)
            return kind switch
            {
                // Values above int/long.MaxValue can appear in hand-edited or migrated backups; keep the exact bit pattern.
                RegistryValueKind.DWord => element.TryGetInt32(out var dword) ? dword : unchecked((int)element.GetUInt32()),
                RegistryValueKind.QWord => element.TryGetInt64(out var qword) ? qword : unchecked((long)element.GetUInt64()),
                RegistryValueKind.Binary => element.GetBytesFromBase64(),
                RegistryValueKind.MultiString => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
                RegistryValueKind.String or RegistryValueKind.ExpandString => element.GetString() ?? string.Empty,
                _ => throw new NotSupportedException($"Registry value kind {kind} cannot be restored automatically.")
            };
        return backup.OriginalValue ?? throw new InvalidDataException("The backup does not contain the original value.");
    }
}
