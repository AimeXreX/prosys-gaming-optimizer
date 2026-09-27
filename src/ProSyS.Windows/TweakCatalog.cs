using System.Text.RegularExpressions;
using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

/// <summary>
/// A small, curated current-user catalog. Every offered entry maps to a documented Windows setting and names where it lives.
/// Only Game Mode is selected by default; everything else is an explicit opt-in.
/// </summary>
public static class TweakCatalog
{
    private const string Gaming = "Gaming & Capture";
    private const string Input = "Input";
    private const string Accessibility = "Accessibility & Input";
    private const string Preferences = "Preferences";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static readonly Curated[] Entries =
    {
        new("gaming.gamebar", GameBar, "AutoGameModeEnabled", 1, RegistryValueKind.DWord, "Game Mode", Gaming,
            "Turns on Windows Game Mode, which gives the game you are playing priority and holds back Windows Update driver installs and restart notifications while you play.",
            "Game Mode is on by default in Windows 11; this turns it back on if it was switched off.",
            "Settings › Gaming › Game Mode (Microsoft Support: \"Use Game Mode while gaming on your Windows device\")", Default: true, BenefitLevel.Low),
        new("gaming.capture", GameDvr, "HistoricalCaptureEnabled", 0, RegistryValueKind.DWord, "Background recording (Record what happened)", Gaming,
            "Stops Game Bar from continuously recording the last minutes of gameplay in the background.",
            "Background recording keeps encoding video while you play, which costs GPU/CPU time and disk writes.",
            "Settings › Gaming › Captures › Record what happened", Default: false, BenefitLevel.Low),
        new("gaming.capture", GameDvr, "AppCaptureEnabled", 0, RegistryValueKind.DWord, "Game Bar screenshots and clips", Gaming,
            "Turns off Game Bar screenshot and video capture for games.",
            "Useful only if you never capture clips with Game Bar.",
            "Settings › Gaming › Captures", Default: false, BenefitLevel.Negligible),
        new("gaming.capture", GameDvr, "AudioCaptureEnabled", 0, RegistryValueKind.DWord, "Record game audio in captures", Gaming,
            "Stops Game Bar from recording game audio together with captured clips.",
            "Removes audio encoding from recordings you do not need sound for.",
            "Settings › Gaming › Captures › Capture audio when recording a game", Default: false, BenefitLevel.Negligible),
        new("gaming.capture", GameDvr, "MicrophoneCaptureEnabled", 0, RegistryValueKind.DWord, "Record microphone in captures", Gaming,
            "Stops Game Bar from recording your microphone in captured clips.",
            "Privacy: prevents voice chat from ending up in shared recordings.",
            "Settings › Gaming › Captures › Capture microphone when recording", Default: false, BenefitLevel.None),
        new("gaming.gamebar", GameBar, "UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord, "Open Game Bar with the controller button", Gaming,
            "Stops the Xbox button on a controller from opening Game Bar.",
            "Avoids Game Bar popping up over the game when the controller button is pressed by accident.",
            "Settings › Gaming › Game Bar", Default: false, BenefitLevel.None),
        new("input.mouse", @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String, "Enhance pointer precision (mouse acceleration)", Input,
            "Turns off mouse acceleration so the pointer moves the same distance for the same hand movement.",
            "Gives consistent aim in games that use the Windows pointer; games using raw input are not affected.",
            "Control Panel › Mouse › Pointer Options › Enhance pointer precision", Default: false, BenefitLevel.Low),
        new("access.sticky", @"Control Panel\Accessibility\StickyKeys", "Flags", "506", RegistryValueKind.String, "Sticky Keys keyboard shortcut", Accessibility,
            "Stops pressing Shift five times from opening the Sticky Keys prompt in the middle of a game. Sticky Keys itself stays available in Settings.",
            "The prompt steals focus from full-screen games.",
            "Settings › Accessibility › Keyboard › Sticky keys › Keyboard shortcut for Sticky keys", Default: false, BenefitLevel.None),
        new("access.toggle", @"Control Panel\Accessibility\ToggleKeys", "Flags", "58", RegistryValueKind.String, "Toggle Keys keyboard shortcut", Accessibility,
            "Stops holding Num Lock for five seconds from turning on Toggle Keys.",
            "Prevents an accidental accessibility prompt during play.",
            "Settings › Accessibility › Keyboard › Toggle keys", Default: false, BenefitLevel.None),
        new("access.keyboard", @"Control Panel\Accessibility\Keyboard Response", "Flags", "122", RegistryValueKind.String, "Filter Keys keyboard shortcut", Accessibility,
            "Stops holding the right Shift key for eight seconds from turning on Filter Keys.",
            "Filter Keys ignores brief or repeated keystrokes, which breaks game input if it turns on by accident.",
            "Settings › Accessibility › Keyboard › Filter keys", Default: false, BenefitLevel.None),
        new("personalize", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegistryValueKind.DWord, "Transparency effects", Preferences,
            "Turns off translucent window and taskbar effects.",
            "A visual preference; can slightly reduce desktop composition work on low-end GPUs.",
            "Settings › Personalization › Colors › Transparency effects", Default: false, BenefitLevel.Negligible),
        new("shell.advanced", ExplorerAdvanced, "Start_IrisRecommendations", 0, RegistryValueKind.DWord, "Tips and new-app recommendations in Start", Preferences,
            "Hides recommendations for tips, shortcuts and new apps in the Start menu.",
            "A preference that removes promotional content from Start.",
            "Settings › Personalization › Start", Default: false, BenefitLevel.None),
        new("shell.advanced", ExplorerAdvanced, "Start_TrackDocs", 0, RegistryValueKind.DWord, "Recently opened items in Start and File Explorer", Preferences,
            "Stops showing recently opened files in Start, Jump Lists and File Explorer.",
            "A privacy preference.",
            "Settings › Personalization › Start › Show recommended files…", Default: false, BenefitLevel.None),
        new("content", ContentDelivery, "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord, "Tips and suggestions notifications", Preferences,
            "Stops Windows from showing tips and suggestions as notifications.",
            "Fewer interruptions while playing.",
            "Settings › System › Notifications › Additional settings › Get tips and suggestions when using Windows", Default: false, BenefitLevel.None),
        new("content", ContentDelivery, "SubscribedContent-338393Enabled", 0, RegistryValueKind.DWord, "Suggested content in Settings", Preferences,
            "Hides suggested content in the Settings app.",
            "A preference that removes promotional content from Settings.",
            "Settings › Privacy & security › General › Show me suggested content in the Settings app", Default: false, BenefitLevel.None),
    };

    public static IReadOnlyList<ITweak> CreateSafeTweaks() => CreateTweaks();

    /// <summary>The capabilities offered to the user.</summary>
    public static IReadOnlyList<ITweak> CreateTweaks()
    {
        var result = Entries.Select(Create).ToList();
        if (result.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidOperationException("Tweak catalog contains duplicate identifiers.");
        return result.AsReadOnly();
    }

    /// <summary>
    /// Offered capabilities plus entries retired from earlier versions. Retired entries are never offered; they exist only so
    /// backups taken by ProSyS 1.0 can still be restored exactly.
    /// </summary>
    public static IReadOnlyList<ITweak> CreateRollbackCatalog()
    {
        var result = new List<ITweak>(CreateTweaks());
        var offered = result.Select(x => x.Metadata.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Retired(result, "gaming.capture", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", dwords: new[] { "AppCaptureEnabled", "HistoricalCaptureEnabled", "AudioCaptureEnabled", "MicrophoneCaptureEnabled", "CursorCaptureEnabled", "EchoCancellationEnabled", "HistoricalCaptureOnBatteryAllowed", "HistoricalCaptureOnWirelessDisplayAllowed", "MaximumRecordLength", "VideoEncodingBitrateMode", "VideoEncodingResolutionMode", "VideoEncodingFrameRateMode", "VKToggleGameBar", "VKMToggleBroadcast", "VKMToggleCameraCapture", "VKMToggleMicrophoneCapture", "VKMToggleRecording" }, strings: Array.Empty<string>());
        Retired(result, "gaming.gamebar", @"Software\Microsoft\GameBar", dwords: new[] { "AutoGameModeEnabled", "AllowAutoGameMode", "ShowStartupPanel", "ShowGameModeNotifications", "GamePanelStartupTipIndex", "UseNexusForGameBarEnabled", "ShowWidgetStoreBadge", "ShowAudioWidget", "ShowCaptureWidget", "ShowGalleryWidget", "ShowLookingForGroupWidget", "ShowPerformanceWidget", "ShowResourcesWidget", "ShowSocialWidget", "ShowXboxChatWidget", "WidgetTransparencyEnabled", "RememberOpenPanels" }, strings: Array.Empty<string>());
        Retired(result, "gaming.config", @"System\GameConfigStore", dwords: new[] { "GameDVR_Enabled", "GameDVR_FSEBehaviorMode", "GameDVR_HonorUserFSEBehaviorMode", "GameDVR_DXGIHonorFSEWindowsCompatible", "GameDVR_EFSEFeatureFlags", "GameDVR_DSEBehavior", "GameDVR_FSEBehavior", "Win32_AutoGameModeDefaultProfile", "Win32_GameModeRelatedProcesses" }, strings: Array.Empty<string>());
        Retired(result, "input.mouse", @"Control Panel\Mouse", dwords: Array.Empty<string>(), strings: new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2", "MouseSensitivity", "MouseHoverTime", "DoubleClickSpeed", "DoubleClickHeight", "DoubleClickWidth", "MouseTrails", "SnapToDefaultButton", "SwapMouseButtons", "ActiveWindowTracking", "Beep" });
        Retired(result, "shell.advanced", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", dwords: new[] { "TaskbarAnimations", "ListviewAlphaSelect", "ListviewShadow", "IconsOnly", "ShowStatusBar", "ShowInfoTip", "ShowCompColor", "ShowEncryptCompressedColor", "HideFileExt", "Hidden", "ShowSuperHidden", "SeparateProcess", "LaunchTo", "NavPaneExpandToCurrentFolder", "NavPaneShowAllFolders", "AutoCheckSelect", "DisablePreviewDesktop", "TaskbarGlomLevel", "MMTaskbarGlomLevel", "TaskbarSmallIcons", "ShowSecondsInSystemClock", "Start_TrackDocs", "Start_TrackProgs", "Start_IrisRecommendations", "Start_AccountNotifications", "ShowTaskViewButton", "TaskbarDa", "TaskbarMn", "TaskbarAl", "EnableSnapBar", "EnableSnapAssistFlyout", "EnableTaskGroups", "PersistBrowsers", "ReindexedProfile" }, strings: Array.Empty<string>());
        Retired(result, "desktop", @"Control Panel\Desktop", dwords: Array.Empty<string>(), strings: new[] { "MenuShowDelay", "AutoEndTasks", "HungAppTimeout", "WaitToKillAppTimeout", "LowLevelHooksTimeout", "ForegroundLockTimeout", "ForegroundFlashCount", "DragFullWindows", "FontSmoothing", "FontSmoothingType", "FontSmoothingGamma", "SmoothScroll", "WheelScrollLines", "WheelScrollChars", "ClickLockTime", "CaretTimeout", "CaretWidth", "CursorBlinkRate", "JPEGImportQuality", "Pattern", "TileWallpaper", "WallpaperStyle", "ScreenSaveActive" });
        Retired(result, "access.keyboard", @"Control Panel\Accessibility\Keyboard Response", dwords: Array.Empty<string>(), strings: new[] { "Flags", "AutoRepeatDelay", "AutoRepeatRate", "BounceTime", "DelayBeforeAcceptance", "Last BounceKey Setting", "Last Valid Delay", "Last Valid Repeat", "Last Valid Wait" });
        Retired(result, "access.sticky", @"Control Panel\Accessibility\StickyKeys", dwords: Array.Empty<string>(), strings: new[] { "Flags", "AudibleFeedback", "HotKeyActive", "HotKeySound", "ConfirmHotKey", "TriState", "TwoKeysOff" });
        Retired(result, "access.toggle", @"Control Panel\Accessibility\ToggleKeys", dwords: Array.Empty<string>(), strings: new[] { "Flags", "HotKeyActive", "HotKeySound", "ConfirmHotKey", "On" });
        Retired(result, "access.mousekeys", @"Control Panel\Accessibility\MouseKeys", dwords: Array.Empty<string>(), strings: new[] { "Flags", "MaximumSpeed", "TimeToMaximumSpeed", "HotKeyActive", "HotKeySound", "MouseKeysOn", "UseCtrlAlt" });
        Retired(result, "personalize", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", dwords: new[] { "EnableTransparency", "AppsUseLightTheme", "SystemUsesLightTheme", "ColorPrevalence", "EnableBlurBehind" }, strings: Array.Empty<string>());
        Retired(result, "dwm", @"Software\Microsoft\Windows\DWM", dwords: new[] { "EnableAeroPeek", "AlwaysHibernateThumbnails", "ColorPrevalence", "AccentColorInactive", "Composition", "ColorizationOpaqueBlend", "EnableWindowColorization", "ForceEffectMode" }, strings: Array.Empty<string>());
        Retired(result, "search", @"Software\Microsoft\Windows\CurrentVersion\Search", dwords: new[] { "SearchboxTaskbarMode", "BingSearchEnabled", "CortanaConsent", "DeviceHistoryEnabled", "HistoryViewEnabled", "SafeSearchMode", "SearchHistoryEnabled", "IsDynamicSearchBoxEnabled" }, strings: Array.Empty<string>());
        Retired(result, "content", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", dwords: new[] { "ContentDeliveryAllowed", "FeatureManagementEnabled", "OemPreInstalledAppsEnabled", "PreInstalledAppsEnabled", "PreInstalledAppsEverEnabled", "SilentInstalledAppsEnabled", "SoftLandingEnabled", "SubscribedContentEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenEnabled", "RotatingLockScreenOverlayEnabled", "RemediationRequired", "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled" }, strings: Array.Empty<string>());
        return result.Where((x, i) => i < offered.Count || !offered.Contains(x.Metadata.Id)).ToList().AsReadOnly();
    }

    public static string IdFor(string groupId, string valueName) => $"{groupId}.{Regex.Replace(valueName, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant()}";

    private static ITweak Create(Curated entry)
    {
        // Control Panel values are read by Windows at sign-in; writing them does not change the live session.
        var requiresSignOut = entry.Path.StartsWith(@"Control Panel\", StringComparison.OrdinalIgnoreCase);
        var risk = entry.Default ? RiskProfile.Safe() : new RiskProfile(RiskLevel.Low, 0, 0, 0, 0, 1, Reversibility.Easy, Confidence.High);
        var metadata = new TweakMetadata(IdFor(entry.Group, entry.ValueName), 3, entry.Name, entry.Description, entry.Category, entry.Why,
            risk, entry.Benefit, EvidenceType.OfficialDocumentation, false, requiresSignOut, false, false, false, new[] { "Windows 11 22000+" },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            "Restore the exact original registry value and type, or remove it (and any key created for it) when it did not exist.",
            "2026-09-27", entry.Default, entry.Reference);
        return new RegistryTweak(metadata, entry.Path, entry.ValueName, entry.Recommended, entry.Kind);
    }

    private static void Retired(List<ITweak> target, string groupId, string path, string[] dwords, string[] strings)
    {
        foreach (var (name, kind) in dwords.Select(x => (x, RegistryValueKind.DWord)).Concat(strings.Select(x => (x, RegistryValueKind.String))))
        {
            var metadata = new TweakMetadata(IdFor(groupId, name), 1, name, "Retired capability kept only to restore older backups.", "Retired",
                "Not offered.", new RiskProfile(RiskLevel.Low, 0, 0, 0, 0, 1, Reversibility.Easy, Confidence.Medium), BenefitLevel.None, EvidenceType.Legacy,
                false, path.StartsWith(@"Control Panel\", StringComparison.OrdinalIgnoreCase), false, false, false, new[] { "Windows 11 22000+" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Restore the exact original registry value and type.", "2026-09-27", false);
            target.Add(new RegistryTweak(metadata, path, name, kind == RegistryValueKind.DWord ? 0 : string.Empty, kind));
        }
    }

    private sealed record Curated(string Group, string Path, string ValueName, object Recommended, RegistryValueKind Kind, string Name, string Category,
        string Description, string Why, string Reference, bool Default, BenefitLevel Benefit);
}
