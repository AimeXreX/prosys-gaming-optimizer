using System.Text.RegularExpressions;
using Microsoft.Win32;
using ProSyS.Core;

namespace ProSyS.Windows;

/// <summary>A conservative current-user catalog. Core changes are selected by default; preference changes are opt-in.</summary>
public static class TweakCatalog
{
    public static IReadOnlyList<ITweak> CreateSafeTweaks() => CreateTweaks();

    public static IReadOnlyList<ITweak> CreateTweaks()
    {
        var result = new List<ITweak>();
        Add(result, "gaming.capture", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "Gaming & Capture", new[]
        {
            D("AppCaptureEnabled", 0, true, 1, 1), D("HistoricalCaptureEnabled", 0, true, 1, 1), D("AudioCaptureEnabled", 0),
            D("MicrophoneCaptureEnabled", 0), D("CursorCaptureEnabled", 0), D("EchoCancellationEnabled", 0),
            D("HistoricalCaptureOnBatteryAllowed", 0), D("HistoricalCaptureOnWirelessDisplayAllowed", 0), D("MaximumRecordLength", 7200),
            D("VideoEncodingBitrateMode", 0), D("VideoEncodingResolutionMode", 0), D("VideoEncodingFrameRateMode", 0),
            D("VKToggleGameBar", 0), D("VKMToggleBroadcast", 0), D("VKMToggleCameraCapture", 0),
            D("VKMToggleMicrophoneCapture", 0), D("VKMToggleRecording", 0)
        });
        Add(result, "gaming.gamebar", @"Software\Microsoft\GameBar", "Gaming & Capture", new[]
        {
            D("AutoGameModeEnabled", 1, true, 1), D("AllowAutoGameMode", 1, true, 1), D("ShowStartupPanel", 0, true),
            D("ShowGameModeNotifications", 0), D("GamePanelStartupTipIndex", 3), D("UseNexusForGameBarEnabled", 0),
            D("ShowWidgetStoreBadge", 0), D("ShowAudioWidget", 0), D("ShowCaptureWidget", 0), D("ShowGalleryWidget", 0),
            D("ShowLookingForGroupWidget", 0), D("ShowPerformanceWidget", 0), D("ShowResourcesWidget", 0),
            D("ShowSocialWidget", 0), D("ShowXboxChatWidget", 0), D("WidgetTransparencyEnabled", 0), D("RememberOpenPanels", 0)
        });
        Add(result, "gaming.config", @"System\GameConfigStore", "Gaming & Capture", new[]
        {
            D("GameDVR_Enabled", 0, true, 1, 1), D("GameDVR_FSEBehaviorMode", 2), D("GameDVR_HonorUserFSEBehaviorMode", 1),
            D("GameDVR_DXGIHonorFSEWindowsCompatible", 1), D("GameDVR_EFSEFeatureFlags", 0), D("GameDVR_DSEBehavior", 2),
            D("GameDVR_FSEBehavior", 2), D("Win32_AutoGameModeDefaultProfile", 1), D("Win32_GameModeRelatedProcesses", 1)
        });
        Add(result, "input.mouse", @"Control Panel\Mouse", "Input", new[]
        {
            S("MouseSpeed", "0"), S("MouseThreshold1", "0"), S("MouseThreshold2", "0"), S("MouseSensitivity", "10"),
            S("MouseHoverTime", "400"), S("DoubleClickSpeed", "500"), S("DoubleClickHeight", "4"), S("DoubleClickWidth", "4"),
            S("MouseTrails", "0"), S("SnapToDefaultButton", "0"), S("SwapMouseButtons", "0"), S("ActiveWindowTracking", "0"), S("Beep", "No")
        });
        Add(result, "shell.advanced", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Windows Shell", new[]
        {
            D("TaskbarAnimations", 0), D("ListviewAlphaSelect", 0), D("ListviewShadow", 0), D("IconsOnly", 1), D("ShowStatusBar", 1),
            D("ShowInfoTip", 1), D("ShowCompColor", 0), D("ShowEncryptCompressedColor", 0), D("HideFileExt", 0), D("Hidden", 1),
            D("ShowSuperHidden", 0), D("SeparateProcess", 1), D("LaunchTo", 1), D("NavPaneExpandToCurrentFolder", 1),
            D("NavPaneShowAllFolders", 0), D("AutoCheckSelect", 0), D("DisablePreviewDesktop", 1), D("TaskbarGlomLevel", 0),
            D("MMTaskbarGlomLevel", 0), D("TaskbarSmallIcons", 0), D("ShowSecondsInSystemClock", 0), D("Start_TrackDocs", 0),
            D("Start_TrackProgs", 0), D("Start_IrisRecommendations", 0), D("Start_AccountNotifications", 0), D("ShowTaskViewButton", 0),
            D("TaskbarDa", 0), D("TaskbarMn", 0), D("TaskbarAl", 0), D("EnableSnapBar", 1), D("EnableSnapAssistFlyout", 1),
            D("EnableTaskGroups", 1), D("PersistBrowsers", 0), D("ReindexedProfile", 1)
        });
        Add(result, "desktop", @"Control Panel\Desktop", "Desktop Responsiveness", new[]
        {
            S("MenuShowDelay", "100"), S("AutoEndTasks", "0"), S("HungAppTimeout", "5000"), S("WaitToKillAppTimeout", "5000"),
            S("LowLevelHooksTimeout", "1000"), S("ForegroundLockTimeout", "200000"), S("ForegroundFlashCount", "3"),
            S("DragFullWindows", "1"), S("FontSmoothing", "2"), S("FontSmoothingType", "2"), S("FontSmoothingGamma", "1500"),
            S("SmoothScroll", "1"), S("WheelScrollLines", "3"), S("WheelScrollChars", "3"), S("ClickLockTime", "1200"),
            S("CaretTimeout", "5000"), S("CaretWidth", "1"), S("CursorBlinkRate", "530"), S("JPEGImportQuality", "100"),
            S("Pattern", ""), S("TileWallpaper", "0"), S("WallpaperStyle", "10"), S("ScreenSaveActive", "0")
        });
        Add(result, "access.keyboard", @"Control Panel\Accessibility\Keyboard Response", "Accessibility & Input", new[]
        {
            S("Flags", "122"), S("AutoRepeatDelay", "1000"), S("AutoRepeatRate", "500"), S("BounceTime", "0"),
            S("DelayBeforeAcceptance", "1000"), S("Last BounceKey Setting", "0"), S("Last Valid Delay", "1000"),
            S("Last Valid Repeat", "500"), S("Last Valid Wait", "1000")
        });
        Add(result, "access.sticky", @"Control Panel\Accessibility\StickyKeys", "Accessibility & Input", new[]
        {
            S("Flags", "506"), S("AudibleFeedback", "0"), S("HotKeyActive", "0"), S("HotKeySound", "0"),
            S("ConfirmHotKey", "0"), S("TriState", "0"), S("TwoKeysOff", "1")
        });
        Add(result, "access.toggle", @"Control Panel\Accessibility\ToggleKeys", "Accessibility & Input", new[]
        { S("Flags", "58"), S("HotKeyActive", "0"), S("HotKeySound", "0"), S("ConfirmHotKey", "0"), S("On", "0") });
        Add(result, "access.mousekeys", @"Control Panel\Accessibility\MouseKeys", "Accessibility & Input", new[]
        {
            S("Flags", "62"), S("MaximumSpeed", "80"), S("TimeToMaximumSpeed", "3000"), S("HotKeyActive", "0"),
            S("HotKeySound", "0"), S("MouseKeysOn", "0"), S("UseCtrlAlt", "1")
        });
        Add(result, "personalize", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "Personalization", new[]
        { D("EnableTransparency", 0), D("AppsUseLightTheme", 0), D("SystemUsesLightTheme", 0), D("ColorPrevalence", 1), D("EnableBlurBehind", 0) });
        Add(result, "dwm", @"Software\Microsoft\Windows\DWM", "Desktop Composition", new[]
        {
            D("EnableAeroPeek", 0), D("AlwaysHibernateThumbnails", 0), D("ColorPrevalence", 1), D("AccentColorInactive", 0),
            D("Composition", 1), D("ColorizationOpaqueBlend", 0), D("EnableWindowColorization", 1), D("ForceEffectMode", 0)
        });
        Add(result, "search", @"Software\Microsoft\Windows\CurrentVersion\Search", "Search & Background", new[]
        {
            D("SearchboxTaskbarMode", 0), D("BingSearchEnabled", 0), D("CortanaConsent", 0), D("DeviceHistoryEnabled", 0),
            D("HistoryViewEnabled", 0), D("SafeSearchMode", 1), D("SearchHistoryEnabled", 0), D("IsDynamicSearchBoxEnabled", 0)
        });
        Add(result, "content", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "Background Content", new[]
        {
            D("ContentDeliveryAllowed", 0), D("FeatureManagementEnabled", 0), D("OemPreInstalledAppsEnabled", 0),
            D("PreInstalledAppsEnabled", 0), D("PreInstalledAppsEverEnabled", 0), D("SilentInstalledAppsEnabled", 0),
            D("SoftLandingEnabled", 0), D("SubscribedContentEnabled", 0), D("SystemPaneSuggestionsEnabled", 0),
            D("RotatingLockScreenEnabled", 0), D("RotatingLockScreenOverlayEnabled", 0), D("RemediationRequired", 0),
            D("SubscribedContent-310093Enabled", 0), D("SubscribedContent-338388Enabled", 0), D("SubscribedContent-338389Enabled", 0),
            D("SubscribedContent-338393Enabled", 0), D("SubscribedContent-353694Enabled", 0), D("SubscribedContent-353696Enabled", 0)
        });

        if (result.Count < 100) throw new InvalidOperationException("The production catalog must expose at least 100 capabilities.");
        if (result.Select(x => x.Metadata.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidOperationException("Tweak catalog contains duplicate identifiers.");
        return result.AsReadOnly();
    }

    private static void Add(ICollection<ITweak> target, string groupId, string path, string category, IEnumerable<Spec> specs)
    {
        foreach (var spec in specs)
        {
            var slug = Regex.Replace(spec.ValueName, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
            var name = Humanize(spec.ValueName);
            var risk = spec.Default ? RiskProfile.Safe(spec.Performance, spec.Latency) :
                new RiskProfile(RiskLevel.Low, spec.Performance, spec.Latency, 0, 0, 2, Reversibility.Easy, Confidence.Medium);
            var metadata = new TweakMetadata($"{groupId}.{slug}", 2, name,
                $"Configure the current-user {name.ToLowerInvariant()} preference.", category,
                "An allowlisted Windows 11 user preference. Measure the result and retain it only when it improves the intended workflow.",
                risk, spec.Performance + spec.Latency > 0 ? BenefitLevel.Low : BenefitLevel.Negligible,
                spec.Default ? EvidenceType.GenerallySupportedBehavior : EvidenceType.Legacy, false, false, false, false, false, new[] { "Windows 11 22000+" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "Restore the exact original registry value and type, or remove it when it did not exist.", "2026-09-25", spec.Default);
            target.Add(new RegistryTweak(metadata, path, spec.ValueName, spec.Recommended, spec.Kind));
        }
    }

    private static string Humanize(string value) => Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ');
    private static Spec D(string name, int value, bool selected = false, int performance = 0, int latency = 0) => new(name, value, RegistryValueKind.DWord, selected, performance, latency);
    private static Spec S(string name, string value, bool selected = false, int performance = 0, int latency = 0) => new(name, value, RegistryValueKind.String, selected, performance, latency);
    private sealed record Spec(string ValueName, object Recommended, RegistryValueKind Kind, bool Default, int Performance, int Latency);
}
