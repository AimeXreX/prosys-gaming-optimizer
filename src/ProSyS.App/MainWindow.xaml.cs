using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Input;
using ProSyS.Core;
using ProSyS.Windows;
using ProSyS.Windows.GameSession;

namespace ProSyS.App;

public partial class MainWindow : Window
{
    private readonly WindowsSystemScanner _scanner = new();
    private readonly IReadOnlyList<ITweak> _tweaks = TweakCatalog.CreateSafeTweaks();
    // Includes entries retired from earlier versions so their backups can still be restored.
    private readonly IReadOnlyList<ITweak> _rollbackTweaks = TweakCatalog.CreateRollbackCatalog();
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProSySOptimizer");
    private readonly ObservableCollection<RecommendationViewModel> _recommendations = new();
    private readonly ObservableCollection<BackupViewModel> _backups = new();
    private readonly ObservableCollection<GameProfile> _gameProfiles = new();
    private readonly ObservableCollection<BenchmarkSummary> _benchmarkHistory = new();
    private readonly UiLocalization _localization = new();
    private readonly CheckBox _profileCpuPriorityCheck = new() { IsChecked = false, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox _profileGpuCheck = new() { IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _profileThrottleCheck = new() { IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _profileCoresCheck = new() { IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly GameSessionManager _sessions;
    private IReadOnlyList<PerformanceAdvice> _advice = Array.Empty<PerformanceAdvice>();
    private readonly Button _cpuBoostButton = new() { Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
    private CpuOptimizationSession? _activeCpuSession;
    private readonly List<ActiveGameSession> _activeGameSessions = new();
    private bool _closingAfterRestore;
    private readonly GameProfileStore _profileStore;
    private OverlayWindow? _overlayWindow;
    private MachineSnapshot? _snapshot;
    private OptimizationPlan? _plan;
    private bool _persian = true;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_root);
        _profileStore = new GameProfileStore(_root);
        _sessions = new GameSessionManager(_root, _tweaks, _rollbackTweaks, new JsonAuditLog(Path.Combine(_root, "Logs", "audit.jsonl")));
        DataPathText.Text = _root;
        RecommendationsList.ItemsSource = _recommendations;
        BackupsList.ItemsSource = _backups;
        GameProfilesList.ItemsSource = _gameProfiles;
        BenchmarkHistoryList.ItemsSource = _benchmarkHistory;
        RecommendationFilter.Foreground = new SolidColorBrush(Color.FromRgb(32, 28, 42));
        if (ProfileOverlayCheck.Parent is Panel profileOptions)
        {
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _profileGpuCheck);
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _profileThrottleCheck);
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _profileCoresCheck);
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _profileCpuPriorityCheck);
            _cpuBoostButton.Style = (Style)FindResource("SecondaryButton");
            _cpuBoostButton.Click += CpuBoost_Click;
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _cpuBoostButton);
        }
        foreach (var category in _tweaks.Select(x => x.Metadata.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x))
            RecommendationFilter.Items.Add(new ComboBoxItem { Content = category, Tag = "category" });
        RefreshHistory();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += (_, _) => _overlayWindow?.Close();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        foreach (var profile in await _profileStore.LoadAsync()) _gameProfiles.Add(profile);
        if (_profileStore.QuarantinedFile is { } quarantined)
            GameSessionStatusText.Text = T($"The saved game profiles were unreadable and were kept at {quarantined}.", $"فایل پروفایل‌های بازی خوانا نبود و در {quarantined} نگه داشته شد.");
        await LoadBenchmarkHistoryAsync();
        var trust = CreateBenchmarkEngine().VerifyTool();
        PresentMonTrustText.Text = trust.Trusted
            ? T("Verified: PresentMon 2.6.0 matches the pinned SHA-256 of the official release.", "تأیید شد: PresentMon نسخه ۲.۶.۰ با هش ثابت نسخه رسمی مطابقت دارد.")
            : T(trust.Message, "ابزار بنچمارک قابل اعتماد نیست: " + trust.Message);
        var recovered = await _sessions.RecoverAsync();
        if (recovered.Count > 0) GameSessionStatusText.Text = T("Recovered an interrupted game session: ", "یک نشست بازی ناتمام بازیابی شد: ") + string.Join(" • ", recovered.Select(x => x.Text(_persian)));
        // The agent is what lets ProSyS optimize games on its own; keep it running whenever the control center opens.
        try { AgentAutostart.TryStart(AgentAutostart.DefaultAgentPath); }
        catch (System.ComponentModel.Win32Exception) { }
        RefreshAgentStatus();
        var incomplete = Engine().FindIncompleteSessions();
        if (incomplete.Count > 0)
        {
            HeaderState.Text = T("RECOVERY ATTENTION", "نیازمند بررسی بازیابی");
            StatusTitle.Text = T($"{incomplete.Count} incomplete session(s) found", $"{incomplete.Count} نشست ناقص پیدا شد");
            StatusSubtitle.Text = T("Open History & Backups to inspect and restore the captured original state.", "برای بررسی و بازگردانی وضعیت اصلی، بخش تاریخچه و پشتیبان را باز کنید.");
            SelectPage(4);
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAndRenderAsync(true);

    private async Task ScanAndRenderAsync(bool switchToDashboard)
    {
        await RunBusyAsync(_persian ? "در حال اسکن سیستم" : "SCANNING SYSTEM", async () =>
        {
            _snapshot = await _scanner.ScanAsync();
            _plan = await new PlanFactory().CreateAsync(_snapshot, _tweaks);
            var snapshot = _snapshot;
            _advice = await Task.Run(() => PerformanceAdvisor.Analyze(snapshot));
            RenderSnapshot(_snapshot);
            RenderRecommendations(_plan);
            RenderAdvice();
            HeaderState.Text = _persian ? "اسکن تکمیل شد" : "SCAN COMPLETE";
            StatusTitle.Text = _plan.Tweaks.Count(x => x.Selected) == 0
                ? (_persian ? "سیستم آماده است" : "SYSTEM READY")
                : (_persian ? $"{_plan.Tweaks.Count(x => x.Selected)} پیشنهاد آماده" : $"{_plan.Tweaks.Count(x => x.Selected)} SAFE CHANGES READY");
            StatusSubtitle.Text = _persian ? "اطلاعات واقعی سیستم و پیشنهادهای قابل بازگشت آماده‌اند." : "Hardware, software and reversible recommendations were refreshed from this PC.";
            QuickPlanButton.IsEnabled = true;
            ApplyButton.IsEnabled = _plan.Tweaks.Any(x => x.Selected);
            LastScanText.Text = DateTime.Now.ToString("g");
            if (switchToDashboard) SelectPage(0);
        });
    }

    private void RenderSnapshot(MachineSnapshot snapshot)
    {
        var insights = snapshot.Insights;
        WindowsValue.Text = $"{snapshot.WindowsVersion}\nBuild {snapshot.WindowsBuild}";
        CpuValue.Text = snapshot.Insights?.Performance is { } performance ? $"{performance.CpuUtilizationPercent:F0}%  •  {snapshot.Cpu}" : snapshot.Cpu;
        MemoryValue.Text = $"{snapshot.TotalMemoryBytes / 1073741824d:F1} GB";
        var available = snapshot.Insights?.AvailableMemoryBytes / 1073741824d ?? 0;
        AvailableMemoryText.Text = $"{available:F1} GB";
        AvailableValue.Text = _plan?.Tweaks.Count(x => x.Current.Status != DetectionStatus.Compliant).ToString() ?? "0";
        var selectedCount = _plan?.Tweaks.Count(x => x.Selected) ?? 0;
        var score = 100 - Math.Min(20, selectedCount * 3);
        if (snapshot.Insights is { } measured && measured.AvailableMemoryBytes < snapshot.TotalMemoryBytes * 0.15) score -= 15;
        if (snapshot.Drives.Any(x => x.IsSystem && x.FreeBytes < x.TotalBytes * 0.10)) score -= 15;
        if (snapshot.Insights?.Gaming.GameModeEnabled == false) score -= 8;
        if (snapshot.Insights?.Gaming.CaptureEnabled == true) score -= 5;
        if (snapshot.AntiCheatProducts.Count > 0 && !snapshot.SecureBoot) score -= 8;
        ReadyScore.Text = $"{Math.Clamp(score, 0, 100)}%";
        OverviewList.ItemsSource = new[]
        {
            new LabelValue(T("GPU", "کارت گرافیک"), snapshot.Gpus.Count == 0 ? T("Unavailable", "در دسترس نیست") : string.Join(", ", snapshot.Gpus.Select(x => x.Name))),
            new LabelValue(T("Storage", "فضای ذخیره‌سازی"), _persian ? $"{snapshot.Drives.Count} درایو • {snapshot.Drives.Sum(x => x.FreeBytes) / 1073741824d:F0} گیگابایت آزاد" : $"{snapshot.Drives.Count} fixed drive(s) • {snapshot.Drives.Sum(x => x.FreeBytes) / 1073741824d:F0} GB free"),
            new LabelValue(T("Network", "شبکه"), insights is null ? T("Unavailable", "در دسترس نیست") : insights.NetworkQuality is { } quality
                ? (_persian ? $"میانگین {quality.AverageLatencyMs:F0} ms • جیتر {quality.JitterMs:F0} ms • {quality.Received}/{quality.Sent} پاسخ" : $"{quality.AverageLatencyMs:F0} ms average • {quality.JitterMs:F0} ms jitter • {quality.Received}/{quality.Sent} replies")
                : (_persian ? $"{insights.NetworkAdapters.Count(x => x.Status == OperationalStatus.Up.ToString())} کارت فعال" : $"{insights.NetworkAdapters.Count(x => x.Status == OperationalStatus.Up.ToString())} active adapter(s)")),
            new LabelValue(T("Active TCP connections", "اتصال‌های فعال پردازش‌ها"), insights?.NetworkConnections is { } connections ? (_persian ? $"{connections.Count} اتصال از {connections.Select(x => x.ProcessId).Distinct().Count()} پردازش" : $"{connections.Count} connections across {connections.Select(x => x.ProcessId).Distinct().Count()} processes") : T("Unavailable", "در دسترس نیست")),
            new LabelValue(T("Power", "پروفایل برق"), snapshot.PowerPlan),
            new LabelValue(T("Secure Boot", "بوت امن"), snapshot.SecureBoot ? T("Enabled", "فعال") : T("Disabled / unavailable", "غیرفعال یا نامشخص")),
            new LabelValue(T("Anti-cheat", "ضدتقلب"), snapshot.AntiCheatProducts.Count == 0 ? T("None detected", "موردی شناسایی نشد") : string.Join(", ", snapshot.AntiCheatProducts))
        };
        HardwareDetails.ItemsSource = new[]
        {
            new LabelValue(T("Operating system", "سیستم‌عامل"), $"{snapshot.WindowsEdition} {snapshot.WindowsVersion} ({snapshot.WindowsBuild})"),
            new LabelValue(T("Architecture", "معماری"), snapshot.Architecture), new LabelValue(T("Processor", "پردازنده"), snapshot.Cpu),
            new LabelValue(T("Logical processors", "پردازنده‌های منطقی"), snapshot.LogicalProcessors.ToString()), new LabelValue(T("Installed memory", "حافظه نصب‌شده"), $"{snapshot.TotalMemoryBytes / 1073741824d:F1} GB"),
            new LabelValue(T("Graphics", "گرافیک"), snapshot.Gpus.Count == 0 ? T("Unavailable", "در دسترس نیست") : string.Join(" • ", snapshot.Gpus.Select(x => $"{x.Name} ({x.DriverVersion})"))),
            new LabelValue(T("Vendor GPU APIs", "رابط رسمی سازنده GPU"), insights?.GpuApis is { } gpuApis ? string.Join(" • ", gpuApis.Where(x => snapshot.Gpus.Any(g => g.Name.Contains(x.Vendor, StringComparison.OrdinalIgnoreCase) || x.Vendor == "AMD" && g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))).Select(x => $"{x.Api}: {(x.Available ? T("available", "آماده") : T("not available", "در دسترس نیست"))}")) : T("Unavailable", "در دسترس نیست")),
            new LabelValue(T("System type", "نوع سیستم"), snapshot.IsLaptop ? T("Laptop", "لپ‌تاپ") : T("Desktop / no battery detected", "دسکتاپ / بدون باتری")), new LabelValue(T("Secure Boot", "بوت امن"), snapshot.SecureBoot ? T("Enabled", "فعال") : T("Disabled / unavailable", "غیرفعال یا نامشخص"))
        };
        if (insights is null) return;
        NetworkList.ItemsSource = insights.NetworkAdapters.Select(x => new NetworkRow(x.Name, _persian
            ? $"{x.Type} • {(x.Status == "Up" ? "فعال" : "غیرفعال")} • {(x.SpeedBitsPerSecond > 0 ? x.SpeedBitsPerSecond / 1_000_000 + " مگابیت" : "سرعت نامشخص")} • دروازه {x.Gateway}"
            : $"{x.Type} • {x.Status} • {(x.SpeedBitsPerSecond > 0 ? x.SpeedBitsPerSecond / 1_000_000 + " Mbps" : "speed unavailable")} • Gateway {x.Gateway}"));
        StartupGrid.ItemsSource = insights.StartupItems.Select(x => new StartupRow(x.Name, T(x.Source, x.Source == "Current user" ? "کاربر فعلی" : "همه کاربران"), x.Command));
        GamesList.ItemsSource = insights.InstalledGames.Count == 0 ? new[] { T("No Steam manifests found", "بازی نصب‌شده‌ای در Steam پیدا نشد") } : insights.InstalledGames;
        ProcessesGrid.ItemsSource = insights.TopProcesses.Select(x => new ProcessRow(x.Id, x.Name, FormatBytes(x.WorkingSetBytes), x.TotalProcessorTime.ToString(@"hh\:mm\:ss"), TranslateClassification(x.Classification)));
        RunningServicesText.Text = insights.Services.Running < 0 ? T("Unavailable", "در دسترس نیست") : $"{insights.Services.Running} / {insights.Services.Total}";
        NetworkUpText.Text = insights.NetworkQuality is { } qualityNow ? $"{qualityNow.AverageLatencyMs:F0} ms" : insights.NetworkAdapters.Count(x => x.Status == OperationalStatus.Up.ToString()).ToString();
        AntiCheatText.Text = snapshot.AntiCheatProducts.Count == 0 ? T("None detected", "موردی شناسایی نشد") : string.Join("\n", snapshot.AntiCheatProducts);
    }

    private void RenderRecommendations(OptimizationPlan plan)
    {
        _recommendations.Clear();
        var catalog = _tweaks.ToDictionary(x => x.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Tweaks)
        {
            var metadata = catalog[item.TweakId].Metadata;
            var row = new RecommendationViewModel(item, metadata, _persian);
            row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(RecommendationViewModel.IsSelected)) UpdatePlanFromSelection(); };
            _recommendations.Add(row);
        }
        ApplyRecommendationFilter();
        UpdatePlanFromSelection();
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index)) SelectPage(index);
    }

    private void SelectPage(int index)
    {
        MainTabs.SelectedIndex = index;
        var englishTitles = new[] { "Dashboard", "System Scan", "Recommendations", "Diagnostics", "History & Backups", "Settings", "Benchmark & A/B", "Game Profiles", "Overlay" };
        var englishCaptions = new[] { "Live system readiness and safe optimization", "Hardware, software and gaming environment", "Explainable and reversible changes", "Processes, memory, services and network", "Session journal and exact-state recovery", "Local preferences and privacy", "Real frame-time capture and comparable results", "Per-game settings with automatic recovery", "Anti-cheat-safe external performance display" };
        var persianTitles = new[] { "داشبورد", "اسکن سیستم", "پیشنهادها", "عیب‌یابی", "تاریخچه و پشتیبان", "تنظیمات", "بنچمارک و مقایسه", "پروفایل بازی‌ها", "نمایشگر زنده" };
        PageTitle.Text = (_persian ? persianTitles : englishTitles)[index];
        PageCaption.Text = _persian ? "داده‌های محلی، شفاف و قابل بازگشت" : englishCaptions[index];
        var buttons = new[] { DashboardNav, ScanNav, RecommendationsNav, DiagnosticsNav, BackupsNav, SettingsNav, BenchmarkNav, GameProfilesNav, OverlayNav };
        foreach (var button in buttons) { button.Background = Brushes.Transparent; button.Foreground = new SolidColorBrush(Color.FromRgb(170, 184, 204)); }
        buttons[index].Background = new SolidColorBrush(Color.FromRgb(20, 40, 58));
        buttons[index].Foreground = (Brush)FindResource("Accent");
        if (index == 4) RefreshHistory();
    }

    private void ReviewPlan_Click(object sender, RoutedEventArgs e) => SelectPage(2);

    private void RecommendationSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyRecommendationFilter();
    private void RecommendationFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyRecommendationFilter();

    private void ApplyRecommendationFilter()
    {
        if (!IsLoaded || RecommendationsList is null) return;
        var search = RecommendationSearch?.Text?.Trim() ?? string.Empty;
        var filter = RecommendationFilter?.SelectedIndex ?? 0;
        var selectedFilter = RecommendationFilter?.SelectedItem as ComboBoxItem;
        var category = selectedFilter?.Tag?.ToString() == "category" ? selectedFilter.Content?.ToString() : null;
        RecommendationsList.ItemsSource = _recommendations.Where(x =>
            (search.Length == 0 || x.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || x.Summary.Contains(search, StringComparison.CurrentCultureIgnoreCase)) &&
            (category is not null ? x.Metadata.Category.Equals(category, StringComparison.CurrentCultureIgnoreCase) :
            filter == 0 || filter == 1 && x.IsSelected || filter == 2 && x.CanSelect || filter == 3 && !x.CanSelect)).ToArray();
    }

    private void RecommendationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecommendationsList.SelectedItem is not RecommendationViewModel item) return;
        DetailName.Text = item.Name;
        DetailDescription.Text = item.Description;
        DetailWhy.Text = _persian ? $"چرا پیشنهاد شده: {item.Why}" : $"Why: {item.Why}";
        DetailEvidence.Text = (_persian ? $"شواهد: {item.Evidence} • اطمینان: {item.Confidence} • اثر احتمالی: {item.Benefit}" : $"Evidence: {item.Evidence} • Confidence: {item.Confidence} • Benefit: {item.Benefit}")
            + (item.Metadata.Reference is { } reference ? (_persian ? $"\nمرجع: {reference}" : $"\nReference: {reference}") : string.Empty);
        DetailRestore.Text = _persian ? $"بازیابی: {item.Restore}" : $"Recovery: {item.Restore}";
        DetailCurrent.Text = _persian ? $"وضعیت فعلی: {item.Plan.Current.Value ?? "تنظیم نشده"} • {item.Compatibility}" : $"Current: {item.Plan.Current.Value ?? "not configured"} • {item.Compatibility}";
    }

    private void UpdatePlanFromSelection()
    {
        if (_plan is null) return;
        var selected = _recommendations.Where(x => x.IsSelected).Select(x => x.Plan.TweakId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _plan = new PlanFactory().Select(_plan, selected);
        PlanHash.Text = T($"Immutable plan {_plan.PlanId}\nSHA-256 {_plan.Sha256[..20]}…\n{selected.Count} selected change(s)",
            $"برنامه تغییر غیرقابل‌دست‌کاری {_plan.PlanId}\nSHA-256 {_plan.Sha256[..20]}…\n{selected.Count} تغییر انتخاب‌شده");
        ApplyButton.IsEnabled = selected.Count > 0;
        AvailableValue.Text = _recommendations.Count(x => x.CanSelect).ToString();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null) { MessageBox.Show(T("Run a system scan first.", "ابتدا اسکن سیستم را اجرا کنید."), "ProSyS", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        UpdatePlanFromSelection();
        var count = _plan.Tweaks.Count(x => x.Selected);
        if (count == 0) { MessageBox.Show(T("Select at least one available recommendation.", "حداقل یک گزینه قابل اعمال را انتخاب کنید."), "ProSyS", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(T($"Apply and verify {count} selected low-risk change(s)? Exact original values will be backed up first.", $"آیا {count} تغییر کم‌ریسک انتخاب‌شده اعمال و بررسی شوند؟ ابتدا مقدار دقیق فعلی پشتیبان‌گیری می‌شود."), T("Confirm optimization", "تأیید بهینه‌سازی"), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await RunBusyAsync("APPLYING & VERIFYING", async () =>
        {
            var result = await Engine().ExecuteAsync(_plan, _tweaks);
            StatusTitle.Text = result.State == OperationState.Completed ? T("OPTIMIZATION VERIFIED", "بهینه‌سازی تأیید شد") : T($"SESSION {result.State.ToString().ToUpperInvariant()}", $"وضعیت نشست: {TranslateOperationState(result.State)}");
            var needsSignOut = result.State == OperationState.Completed && _plan.Tweaks.Any(x => x.Selected && _tweaks.Any(t => t.Metadata.RequiresSignOut && t.Metadata.Id == x.TweakId));
            StatusSubtitle.Text = T($"{result.Results.Count(x => x.Success)} verified • Recovery data saved locally.", $"{result.Results.Count(x => x.Success)} تغییر تأیید شد • داده بازیابی به‌صورت محلی ذخیره شد.")
                + (needsSignOut ? T(" Some changes take effect after you sign out.", " برخی تغییرات پس از خروج و ورود دوباره به حساب اعمال می‌شوند.") : string.Empty);
            RefreshHistory();
            _snapshot = await _scanner.ScanAsync();
            _plan = await new PlanFactory().CreateAsync(_snapshot, _tweaks);
            RenderSnapshot(_snapshot);
            RenderRecommendations(_plan);
        });
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => await ScanAndRenderAsync(false);

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null || _plan is null) { MessageBox.Show("Run a system scan before exporting.", "ProSyS", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var reports = Path.Combine(_root, "Reports");
        Directory.CreateDirectory(reports);
        var path = Path.Combine(reports, $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { schemaVersion = 1, snapshot = _snapshot, plan = _plan }, new JsonSerializerOptions { WriteIndented = true }));
        var htmlPath = Path.ChangeExtension(path, ".html");
        await ReportExporter.ExportHtmlAsync(htmlPath, _snapshot, _plan);
        MessageBox.Show(T($"JSON and readable HTML reports saved locally:\n{path}\n{htmlPath}", $"گزارش JSON و نسخه خوانای HTML ذخیره شد:\n{path}\n{htmlPath}"), "ProSyS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();
    private void RefreshHistory()
    {
        _backups.Clear();
        var directory = Path.Combine(_root, "Backups");
        if (!Directory.Exists(directory)) return;
        foreach (var folder in new DirectoryInfo(directory).GetDirectories().OrderByDescending(x => x.LastWriteTimeUtc))
        {
            var journal = Path.Combine(folder.FullName, "journal.json");
            var state = "Backup available";
            var count = 0;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(journal));
                if (document.RootElement.TryGetProperty("State", out var stateValue))
                    state = stateValue.ValueKind == JsonValueKind.Number && stateValue.TryGetInt32(out var stateNumber) && Enum.IsDefined(typeof(OperationState), stateNumber)
                        ? ((OperationState)stateNumber).ToString() : stateValue.ToString();
                if (document.RootElement.TryGetProperty("Results", out var results)) count = results.GetArrayLength();
            }
            catch (Exception) when (File.Exists(journal)) { state = "Journal unreadable"; }
            _backups.Add(new(folder.FullName, folder.LastWriteTime, state, count));
        }
    }

    private void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = BackupsList.SelectedItem as BackupViewModel;
        RestoreSelectedButton.IsEnabled = item is not null;
        BackupDetail.Text = item is null ? T("Choose a session to inspect its recovery state.", "یک نشست را برای مشاهده وضعیت بازیابی انتخاب کنید.") :
            T($"Session: {item.Title}\nState: {item.State}\nRecorded operations: {item.OperationCount}\n\nRollback restores captured values, never assumed defaults.",
              $"نشست: {item.Title}\nوضعیت: {TranslateOperationState(item.State)}\nعملیات ثبت‌شده: {item.OperationCount}\n\nبازیابی مقدار ثبت‌شده واقعی را برمی‌گرداند، نه یک مقدار پیش‌فرض فرضی.");
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupViewModel item) return;
        if (MessageBox.Show(T("Restore the exact original values recorded for this session?", "مقادیر دقیق ثبت‌شده پیش از این نشست بازگردانده شوند؟"), T("Confirm rollback", "تأیید بازیابی"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await RunBusyAsync("RESTORING ORIGINAL STATE", async () =>
        {
            var result = await Engine().RollbackAsync(item.Directory, _rollbackTweaks, default, TweakCatalog.ResolveDynamic);
            HeaderState.Text = _persian ? TranslateOperationState(result.State) : result.State.ToString().ToUpperInvariant();
            BackupDetail.Text = T($"Rollback verification: {result.Results.Count(x => x.Success)}/{result.Results.Count} passed.", $"بررسی بازیابی: {result.Results.Count(x => x.Success)} از {result.Results.Count} مورد موفق بود.");
            RefreshHistory();
        });
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        _persian = !_persian;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        RootLayout.FlowDirection = _persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _localization.Apply(RootLayout, _persian);
        LanguageButton.Content = _persian ? "EN" : "فارسی";
        _profileGpuCheck.Content = T("Use the high-performance GPU for this game (laptops with two GPUs)", "استفاده از GPU پرقدرت برای این بازی (لپ‌تاپ‌های دارای دو کارت گرافیک)");
        _profileThrottleCheck.Content = T("Throttle browsers, sync clients and launchers while playing (EcoQoS; restored on exit)", "محدودسازی مرورگرها، همگام‌سازها و لانچرها در زمان بازی (EcoQoS؛ پس از خروج بازگردانده می‌شود)");
        _profileCoresCheck.Content = T("Prefer performance cores on hybrid Intel CPUs (confirm with the A/B test)", "اولویت هسته‌های قدرتمند در پردازنده‌های هیبریدی Intel (با آزمون A/B بسنجید)");
        RenderAdvice();
        _profileCpuPriorityCheck.Content = T("Raise game priority to Above normal (never High or Realtime; not allowed by some anti-cheat)", "افزایش اولویت بازی به Above normal (هرگز High یا Realtime؛ برخی ضدتقلب‌ها اجازه نمی‌دهند)");
        _cpuBoostButton.Content = T("ATTACH CPU BOOST TO THE RUNNING GAME", "اتصال تقویت پردازنده به بازی در حال اجرا");
        var startupHeaders = _persian ? new[] { "نام", "منبع", "فایل اجرایی" } : new[] { "Name", "Source", "Executable" };
        for (var i = 0; i < StartupGrid.Columns.Count; i++) StartupGrid.Columns[i].Header = startupHeaders[i];
        var processHeaders = _persian ? new[] { "پردازش", "شناسه", "حافظه", "زمان پردازنده", "دسته" } : new[] { "Process", "PID", "Memory", "CPU time", "Class" };
        for (var i = 0; i < ProcessesGrid.Columns.Count; i++) ProcessesGrid.Columns[i].Header = processHeaders[i];
        SelectPage(MainTabs.SelectedIndex);
        if (_plan is not null) RenderRecommendations(_plan);
        if (_snapshot is not null) RenderSnapshot(_snapshot);
        RefreshBenchmarkDisplay();
    }

    private BenchmarkEngine CreateBenchmarkEngine() => new(Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon", "PresentMon.exe"), _root);

    private async void StartBenchmark_Click(object sender, RoutedEventArgs e)
    {
        var processName = Path.GetFileName(BenchmarkProcessText.Text.Trim());
        if (string.IsNullOrWhiteSpace(processName)) { MessageBox.Show(T("Enter the game process name, for example game.exe.", "نام پردازش بازی را وارد کنید؛ برای نمونه game.exe."), "ProSyS"); return; }
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) processName += ".exe";
        var duration = int.TryParse((BenchmarkDuration.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var seconds) ? seconds : 30;
        await RunBusyAsync(T("CAPTURING FRAME TIMES", "در حال ثبت زمان فریم‌ها"), async () =>
        {
            _snapshot ??= await _scanner.ScanAsync();
            var result = await CreateBenchmarkEngine().CaptureAsync(processName, duration, _snapshot);
            result = result with { IsBaseline = BenchmarkBaselineCheck.IsChecked == true };
            _benchmarkHistory.Insert(0, result);
            await SaveBenchmarkHistoryAsync();
            RefreshBenchmarkDisplay();
        });
    }

    private void CompareBenchmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_benchmarkHistory.FirstOrDefault() is not { } latest) { MessageBox.Show(T("Capture benchmark runs first.", "ابتدا اجراهای بنچمارک را ثبت کنید."), "ProSyS"); return; }
        // Baseline runs (captured with "Baseline run" checked) are compared with the other runs of the same game.
        var game = _benchmarkHistory.Where(x => x.GameProcess.Equals(latest.GameProcess, StringComparison.OrdinalIgnoreCase)).ToList();
        var before = game.Where(x => x.IsBaseline).Take(10).ToList();
        var after = game.Where(x => !x.IsBaseline).Take(10).ToList();
        var comparison = BenchmarkEngine.CompareSets(before, after);
        string Verdict(BenchmarkVerdict verdict) => verdict switch
        {
            BenchmarkVerdict.Improvement => T("Improvement (95% confidence)", "بهبود (اطمینان ۹۵٪)"),
            BenchmarkVerdict.Regression => T("REGRESSION (95% confidence)", "افت عملکرد (اطمینان ۹۵٪)"),
            BenchmarkVerdict.NoSignificantChange => T("No significant change", "تفاوت معناداری نیست"),
            BenchmarkVerdict.InsufficientRuns => T($"Need at least {BenchmarkEngine.MinimumRunsPerSide} baseline and {BenchmarkEngine.MinimumRunsPerSide} after runs", $"دست‌کم {BenchmarkEngine.MinimumRunsPerSide} اجرای پایه و {BenchmarkEngine.MinimumRunsPerSide} اجرای بعد از تغییر لازم است"),
            _ => T("Not comparable", "قابل مقایسه نیست")
        };
        string Metric(string label, MetricChange? change) => change is null ? string.Empty
            : $"\n{label}: {change.DeltaPercent:+0.0;-0.0;0}%  [95% CI {change.CiLowPercent:+0.0;-0.0;0}% … {change.CiHighPercent:+0.0;-0.0;0}%]  → {Verdict(change.Verdict)}";
        BenchmarkResultText.Text = T($"{latest.GameProcess}: {comparison.BeforeRuns} baseline vs {comparison.AfterRuns} after runs", $"{latest.GameProcess}: {comparison.BeforeRuns} اجرای پایه در برابر {comparison.AfterRuns} اجرای بعد")
            + Metric(T("Average FPS", "میانگین FPS"), comparison.AverageFps)
            + Metric(T("1% low", "یک درصد پایین"), comparison.OnePercentLowFps)
            + $"\n{T("Result", "نتیجه")}: {Verdict(comparison.Verdict)}\n{comparison.Reason}";
    }

    private async Task LoadBenchmarkHistoryAsync()
    {
        var path = Path.Combine(_root, "Benchmarks", "history.json");
        if (!File.Exists(path)) return;
        try
        {
            var items = JsonSerializer.Deserialize<List<BenchmarkSummary>>(await File.ReadAllTextAsync(path)) ?? new();
            foreach (var item in items.OrderByDescending(x => x.CapturedAt)) _benchmarkHistory.Add(item);
            RefreshBenchmarkDisplay();
        }
        catch (JsonException)
        {
            // Move the unreadable file aside so the next save cannot overwrite it.
            File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, $"history.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"), true);
            BenchmarkResultText.Text = T("Benchmark history is unreadable; it was kept as a separate file and captures were left untouched.", "تاریخچه بنچمارک خوانا نیست؛ به‌صورت فایل جداگانه نگه داشته شد و فایل‌های ثبت‌شده دست‌نخورده باقی ماندند.");
        }
    }

    private async Task SaveBenchmarkHistoryAsync()
    {
        var folder = Path.Combine(_root, "Benchmarks"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "history.json"); var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_benchmarkHistory, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    private void RefreshBenchmarkDisplay()
    {
        BenchmarkHistoryList.ItemsSource = _benchmarkHistory.Select(x => $"{x.CapturedAt.LocalDateTime:g}  •  {x.GameProcess}  •  {x.AverageFps:F1} FPS  •  1% {x.OnePercentLowFps:F1}").ToArray();
        if (_benchmarkHistory.FirstOrDefault() is not { } latest) return;
        BenchmarkResultText.Text = _persian
            ? $"{latest.GameProcess}\nمیانگین: {latest.AverageFps:F1} FPS\nیک درصد پایین: {latest.OnePercentLowFps:F1}\n۰.۱ درصد پایین: {latest.PointOnePercentLowFps:F1}\nمیانه زمان فریم: {latest.MedianFrameTimeMs:F2} ms\nP99: {latest.P99FrameTimeMs:F2} ms\n{latest.FrameCount:N0} فریم"
            : $"{latest.GameProcess}\nAverage: {latest.AverageFps:F1} FPS\n1% low: {latest.OnePercentLowFps:F1}\n0.1% low: {latest.PointOnePercentLowFps:F1}\nMedian frame time: {latest.MedianFrameTimeMs:F2} ms\nP99: {latest.P99FrameTimeMs:F2} ms\n{latest.FrameCount:N0} frames";
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameText.Text.Trim();
        var process = Path.GetFileName(ProfileProcessText.Text.Trim());
        var path = string.IsNullOrWhiteSpace(ProfilePathText.Text) ? null : Path.GetFullPath(ProfilePathText.Text.Trim());
        if (name.Length is < 1 or > 80 || process.Length is < 1 or > 128) { MessageBox.Show(T("Enter a profile name and game process.", "نام پروفایل و پردازش بازی را وارد کنید."), "ProSyS"); return; }
        if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process += ".exe";
        if (path is not null && !File.Exists(path)) { MessageBox.Show(T("The executable path does not exist.", "مسیر فایل اجرایی وجود ندارد."), "ProSyS"); return; }
        var kind = (OptimizationProfileKind)Math.Clamp(ProfileKindBox.SelectedIndex, 0, 2);
        var ids = kind switch
        {
            OptimizationProfileKind.Safe => _tweaks.Where(x => new RiskEngine().AllowedInSafeProfile(x.Metadata)).Select(x => x.Metadata.Id).ToArray(),
            // Balanced: gaming/capture settings only. Competitive: also input and accessibility shortcuts. Preferences are never part of a game profile.
            OptimizationProfileKind.Balanced => _tweaks.Where(x => x.Metadata.Category == "Gaming & Capture").Select(x => x.Metadata.Id).ToArray(),
            _ => _tweaks.Where(x => x.Metadata.Category != "Preferences").Select(x => x.Metadata.Id).ToArray()
        };
        var current = GameProfilesList.SelectedItem as GameProfile;
        var profile = new GameProfile(current?.Id ?? Guid.NewGuid(), name, process, path, kind, ids, ProfileRestoreCheck.IsChecked == true, ProfileOverlayCheck.IsChecked == true, DateTimeOffset.UtcNow, _profileCpuPriorityCheck.IsChecked == true, _profileGpuCheck.IsChecked == true,
            _profileThrottleCheck.IsChecked == true, _profileCoresCheck.IsChecked == true);
        if (current is not null) _gameProfiles[_gameProfiles.IndexOf(current)] = profile; else _gameProfiles.Add(profile);
        await _profileStore.SaveAsync(_gameProfiles);
        GameProfilesList.SelectedItem = profile;
        GameSessionStatusText.Text = T("Profile saved locally.", "پروفایل به‌صورت محلی ذخیره شد.");
    }

    private void GameProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameProfilesList.SelectedItem is not GameProfile profile) return;
        ProfileNameText.Text = profile.Name; ProfileProcessText.Text = profile.GameProcess; ProfilePathText.Text = profile.ExecutablePath ?? string.Empty;
        ProfileKindBox.SelectedIndex = Math.Clamp((int)profile.Profile, 0, 2); ProfileRestoreCheck.IsChecked = profile.RestoreOnExit; ProfileOverlayCheck.IsChecked = profile.OverlayEnabled; _profileCpuPriorityCheck.IsChecked = profile.CpuPriorityEnabled; _profileGpuCheck.IsChecked = profile.HighPerformanceGpu;
        _profileThrottleCheck.IsChecked = profile.ThrottleBackground; _profileCoresCheck.IsChecked = profile.PreferPerformanceCores;
    }

    private void CpuBoost_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCpuSession is not null)
        {
            MessageBox.Show(T("CPU prioritization is already attached to a running game.", "اولویت‌دهی پردازنده از قبل به یک بازی در حال اجرا متصل است."), "ProSyS");
            return;
        }
        var processName = Path.GetFileNameWithoutExtension(ProfileProcessText.Text.Trim());
        if (string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show(T("Enter the game process in the profile form (for example cs2.exe), then try again.", "پردازش بازی را در فرم پروفایل وارد کنید (برای نمونه cs2.exe) و دوباره تلاش کنید."), "ProSyS");
            return;
        }
        var game = Process.GetProcessesByName(processName).FirstOrDefault(x => !x.HasExited);
        if (game is null)
        {
            MessageBox.Show(T($"{processName} is not running. Start the game, then try again.", $"{processName} در حال اجرا نیست. بازی را اجرا کنید و دوباره تلاش کنید."), "ProSyS");
            return;
        }
        try
        {
            var session = new ProcessCpuOptimizer().Apply(game);
            _activeCpuSession = session;
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(_activeCpuSession, session)) return;
                _activeCpuSession = null;
                GameSessionStatusText.Text = T($"{processName} exited; the per-process CPU policy ended automatically.", $"{processName} بسته شد؛ سیاست پردازنده مخصوص همان پردازش خودکار پایان یافت.");
            });
            GameSessionStatusText.Text = T($"{processName} (PID {game.Id}) now runs at Above normal priority. High and Realtime are intentionally not used.",
                $"{processName} (پردازش {game.Id}) اکنون با اولویت Above normal اجرا می‌شود. حالت‌های High و Realtime عمداً استفاده نمی‌شوند.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _activeCpuSession = null;
            MessageBox.Show(T("The game's priority was not changed: ", "اولویت بازی تغییر نکرد: ") + ex.Message, "ProSyS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void LaunchProfile_Click(object sender, RoutedEventArgs e)
    {
        if (GameProfilesList.SelectedItem is not GameProfile profile) { MessageBox.Show(T("Select a saved profile first.", "ابتدا یک پروفایل ذخیره‌شده را انتخاب کنید."), "ProSyS"); return; }
        var processBaseName = Path.GetFileNameWithoutExtension(profile.GameProcess);
        var runningGame = Process.GetProcessesByName(processBaseName).FirstOrDefault(x => !x.HasExited);
        if (runningGame is null && (string.IsNullOrWhiteSpace(profile.ExecutablePath) || !File.Exists(profile.ExecutablePath))) { MessageBox.Show(T("Start the game first or provide a valid executable path.", "ابتدا بازی را اجرا کنید یا مسیر معتبر فایل اجرایی را وارد کنید."), "ProSyS"); return; }
        await RunBusyAsync(T("PREPARING GAME SESSION", "در حال آماده‌سازی نشست بازی"), async () =>
        {
            var snapshot = await _scanner.ScanAsync();
            // The GPU preference is keyed by the executable path and is read when the game starts, so the profile is applied before launch.
            var executable = profile.ExecutablePath ?? TryGetExecutablePath(runningGame);
            var session = await _sessions.StartAsync(profile, snapshot, executable,
                _ => runningGame is not null ? Task.FromResult(runningGame) : StartAndFindGameAsync(profile.ExecutablePath!, processBaseName),
                gameAlreadyRunning: runningGame is not null);
            if (session is null)
            {
                GameSessionStatusText.Text = T("The background agent is already optimizing a game session.", "عامل پس‌زمینه از قبل در حال بهینه‌سازی یک نشست بازی است.");
                return;
            }
            var game = session.Game;
            if (profile.OverlayEnabled) ShowOverlay();
            GameSessionStatusText.Text = $"{profile.Name} (PID {game.Id})\n" + string.Join("\n", session.StartOutcomes.Select(FormatOutcome)) +
                (session.ThrottledCount > 0 ? "\n" + T($"✓ {session.ThrottledCount} background apps throttled", $"✓ {session.ThrottledCount} برنامه پس‌زمینه محدود شد") : string.Empty);
            _activeGameSessions.Add(session);
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.InvokeAsync(() => EndGameSessionAsync(session, gameExited: true));
            if (game.HasExited) await EndGameSessionAsync(session, gameExited: true);
        });
    }

    private string FormatOutcome(LeverOutcome outcome) => (outcome.Status switch { LeverStatus.Applied => "✓ ", LeverStatus.AlreadyOptimal => "• ", LeverStatus.Skipped => "– ", _ => "⚠ " }) + outcome.Text(_persian);

    private async void RunAbTest_Click(object sender, RoutedEventArgs e)
    {
        var processName = Path.GetFileName(BenchmarkProcessText.Text.Trim());
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) processName += ".exe";
        var game = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).FirstOrDefault(x => !x.HasExited);
        if (game is null) { MessageBox.Show(T("Start the game and enter a repeatable scene first.", "ابتدا بازی را اجرا کنید و در یک صحنه ثابت و تکرارپذیر قرار بگیرید."), "ProSyS"); return; }
        if (_activeGameSessions.Count > 0) { MessageBox.Show(T("End the active game session first.", "ابتدا نشست بازی فعال را پایان دهید."), "ProSyS"); return; }
        // The GPU choice only takes effect at launch, so it is not part of an in-game A/B comparison.
        var profile = (_gameProfiles.FirstOrDefault(x => x.GameProcess.Equals(processName, StringComparison.OrdinalIgnoreCase)) ?? GameDetector.CreateAutoProfile(processName, null)) with { HighPerformanceGpu = false };
        var duration = int.TryParse((BenchmarkDuration.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var seconds) ? seconds : 30;
        var runs = int.TryParse((AbRunsBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var r) ? r : BenchmarkEngine.MinimumRunsPerSide;
        ActiveGameSession? session = null;
        await RunBusyAsync(T("RUNNING A/B TEST", "در حال اجرای آزمون A/B"), async () =>
        {
            var snapshot = _snapshot ?? await _scanner.ScanAsync();
            var progress = new Progress<string>(step => AbResultText.Text = T($"Capturing {step}… keep the scene still.", $"ثبت {step}… صحنه را ثابت نگه دارید."));
            var result = await new AbTestRunner(CreateBenchmarkEngine()).RunAsync(processName, duration, runs, snapshot,
                async ct => session ??= await _sessions.StartAsync(profile, snapshot, null, _ => Task.FromResult(game), gameAlreadyRunning: true, ct)
                    ?? throw new InvalidOperationException(T("The background agent is optimizing this game; pause it from the tray icon first.", "عامل پس‌زمینه در حال بهینه‌سازی این بازی است؛ ابتدا آن را از آیکن کنار ساعت متوقف کنید.")),
                async _ => { if (session is not null) { await session.EndAsync(); session = null; } }, progress);
            foreach (var capture in result.Baseline.Concat(result.Optimized).OrderBy(x => x.CapturedAt)) _benchmarkHistory.Insert(0, capture);
            await SaveBenchmarkHistoryAsync();
            RefreshBenchmarkDisplay();
            var c = result.Comparison;
            string Metric(string label, MetricChange? change) => change is null ? string.Empty
                : $"\n{label}: {change.DeltaPercent:+0.0;-0.0;0}%  [95% CI {change.CiLowPercent:+0.0;-0.0;0}% … {change.CiHighPercent:+0.0;-0.0;0}%]";
            AbResultText.Text = T($"{processName}: {c.BeforeRuns} baseline vs {c.AfterRuns} optimized runs", $"{processName}: {c.BeforeRuns} اجرای پایه در برابر {c.AfterRuns} اجرای بهینه")
                + Metric(T("Average FPS", "میانگین FPS"), c.AverageFps) + Metric(T("1% low", "یک درصد پایین"), c.OnePercentLowFps) + "\n" + c.Verdict switch
                {
                    BenchmarkVerdict.Improvement => T("IMPROVEMENT — keep these session boosts for this game.", "بهبود واقعی — این بوست‌ها را برای این بازی نگه دارید."),
                    BenchmarkVerdict.Regression => T("REGRESSION — turn the session boosts off for this game.", "افت کارایی — بوست‌های نشست را برای این بازی خاموش کنید."),
                    BenchmarkVerdict.NoSignificantChange => T("No significant change — the game is GPU-bound or already optimal.", "تفاوت معناداری نیست — بازی به GPU وابسته است یا از قبل بهینه است."),
                    _ => T("Not comparable — " + c.Reason, "قابل مقایسه نیست — " + c.Reason)
                };
        });
    }

    private void StartAgent_Click(object sender, RoutedEventArgs e)
    {
        if (!AgentAutostart.TryStart(AgentAutostart.DefaultAgentPath)) MessageBox.Show(T("ProSyS.Agent.exe is missing from this build.", "فایل ProSyS.Agent.exe در این نسخه وجود ندارد."), "ProSyS");
        RefreshAgentStatus();
    }

    private void AgentStartup_Click(object sender, RoutedEventArgs e)
    {
        AgentAutostart.Set(AgentAutostart.DefaultAgentPath, AgentStartupCheck.IsChecked == true);
        RefreshAgentStatus();
    }

    private async void RefreshAgentStatus()
    {
        await Task.Delay(600);
        AgentStartupCheck.IsChecked = AgentAutostart.IsEnabled(AgentAutostart.DefaultAgentPath);
        AgentStatusText.Text = AgentAutostart.IsRunning() ? T("● Agent is running in the system tray", "● عامل در سینی سیستم فعال است") : T("○ Agent is not running", "○ عامل فعال نیست");
    }

    private static string? TryGetExecutablePath(Process? process)
    {
        // Protected (anti-cheat) processes deny module access; the profile's executable path is then required for the GPU preference.
        try { return process?.MainModule?.FileName; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private void RenderAdvice()
    {
        AdvisorList.ItemsSource = _advice.Select(x => new AdviceRow(_persian ? x.TitleFa : x.Title, _persian ? x.DetailFa : x.Detail)).ToArray();
        AdvisorEmptyText.Visibility = _advice.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_snapshot is not null && _advice.Count == 0)
            AdvisorEmptyText.Text = T("No common FPS limiters were found.", "محدودکننده رایجی برای FPS پیدا نشد.");
    }

    public sealed record AdviceRow(string Title, string Detail);

    /// <summary>
    /// Starts the executable and waits for the real game process. Launchers (Steam, Epic, etc.) often start
    /// the game as a different process and exit immediately, so the started process itself is not tracked.
    /// </summary>
    private static async Task<Process> StartAndFindGameAsync(string executablePath, string processBaseName)
    {
        var launched = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            if (launched is { HasExited: false } && launched.ProcessName.Equals(processBaseName, StringComparison.OrdinalIgnoreCase)) return launched;
            var game = Process.GetProcessesByName(processBaseName).FirstOrDefault(x => !x.HasExited);
            if (game is not null) return game;
            await Task.Delay(1000);
        }
        throw new InvalidOperationException($"{processBaseName} did not start within two minutes; profile changes were restored.");
    }

    private async Task EndGameSessionAsync(ActiveGameSession session, bool gameExited)
    {
        if (!_activeGameSessions.Remove(session)) return;
        try
        {
            var outcomes = await session.EndAsync();
            var prefix = gameExited ? T("Game exited. Restored:", "بازی بسته شد. بازگردانی:") : T("ProSyS is closing. Restored:", "ProSyS در حال بسته‌شدن است. بازگردانی:");
            GameSessionStatusText.Text = prefix + "\n" + string.Join("\n", outcomes.Select(FormatOutcome).DefaultIfEmpty(T("Nothing needed restoring.", "چیزی برای بازگردانی نبود.")));
        }
        catch (Exception ex)
        {
            GameSessionStatusText.Text = T("Automatic restore failed; use History & Backups to restore manually. ", "بازیابی خودکار ناموفق بود؛ از بخش تاریخچه و پشتیبان به‌صورت دستی بازیابی کنید. ") + ex.Message;
        }
        finally
        {
            if (_activeGameSessions.Count == 0) HideOverlay();
            RefreshHistory();
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterRestore || (_activeGameSessions.Count == 0 && _activeCpuSession is null)) return;
        // Restore every game session before exiting; otherwise its changes would stay applied after ProSyS closes.
        e.Cancel = true;
        IsEnabled = false;
        foreach (var session in _activeGameSessions.ToArray()) await EndGameSessionAsync(session, gameExited: false);
        _activeCpuSession?.TryRestore();
        _activeCpuSession = null;
        _closingAfterRestore = true;
        Close();
    }

    private void ShowOverlay_Click(object sender, RoutedEventArgs e) => ShowOverlay();
    private void HideOverlay_Click(object sender, RoutedEventArgs e) => HideOverlay();
    private void ShowOverlay()
    {
        if (_overlayWindow is null) { _overlayWindow = new OverlayWindow(); _overlayWindow.Closed += (_, _) => { _overlayWindow = null; OverlayStatusText.Text = T("Stopped", "متوقف"); }; _overlayWindow.Show(); }
        OverlayStatusText.Text = T("Running — external and injection-free", "فعال — خارجی و بدون تزریق به بازی");
    }
    private void HideOverlay() { _overlayWindow?.Close(); _overlayWindow = null; OverlayStatusText.Text = T("Stopped", "متوقف"); }

    private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        var helper = new[] { Path.Combine(AppContext.BaseDirectory, "Tools", "PrivilegedHelper", "ProSyS.PrivilegedHelper.exe"), Path.Combine(AppContext.BaseDirectory, "ProSyS.PrivilegedHelper.exe") }.FirstOrDefault(File.Exists) ?? string.Empty;
        if (!File.Exists(helper)) { RestorePointStatusText.Text = T("Administrator helper is missing from this build.", "ابزار مدیریتی در این نسخه پیدا نشد."); return; }
        try
        {
            var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("create-restore-point"); start.ArgumentList.Add("ProSyS before optimization");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Helper could not be started.");
            await process.WaitForExitAsync();
            RestorePointStatusText.Text = process.ExitCode switch
            {
                0 => T("Windows restore point created and verified.", "نقطه بازیابی ویندوز ساخته و تأیید شد."),
                3 => T("Windows did not create a new restore point because one was created in the last 24 hours.", "ویندوز نقطه بازیابی جدیدی نساخت، چون در ۲۴ ساعت گذشته یکی ساخته شده است."),
                4 => T("System Protection is turned off for the system drive; enable it in System Properties to create restore points.", "محافظت سیستم برای درایو ویندوز خاموش است؛ برای ساخت نقطه بازیابی آن را در System Properties فعال کنید."),
                _ => T($"Restore point failed (code {process.ExitCode}).", $"ساخت نقطه بازیابی ناموفق بود (کد {process.ExitCode}).")
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { RestorePointStatusText.Text = T("Administrator request was cancelled.", "درخواست دسترسی مدیر لغو شد."); }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; await ScanAndRenderAsync(true); return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { e.Handled = true; Language_Click(this, new RoutedEventArgs()); return; }
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        var index = e.SystemKey switch { Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, Key.D5 => 4, Key.D6 => 5, Key.D7 => 6, Key.D8 => 7, Key.D9 => 8, _ => -1 };
        if (index >= 0) { e.Handled = true; SelectPage(index); }
    }

    private async Task RunBusyAsync(string title, Func<Task> action)
    {
        IsEnabled = false;
        HeaderState.Text = title;
        try { await action(); }
        catch (Exception ex) { HeaderState.Text = "SAFE FAILURE"; MessageBox.Show(ex.Message, "ProSyS", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsEnabled = true; }
    }

    private OptimizationEngine Engine() => new(_root, new JsonAuditLog(Path.Combine(_root, "Logs", "audit.jsonl")));
    private static string FormatBytes(long value) => value >= 1073741824 ? $"{value / 1073741824d:F1} GB" : $"{value / 1048576d:F0} MB";
    private string T(string english, string persian) => _persian ? persian : english;
    private static string TranslateOperationState(OperationState state) => state switch
    {
        OperationState.Created => "ایجادشده", OperationState.Analyzed => "تحلیل‌شده", OperationState.Planned => "برنامه‌ریزی‌شده",
        OperationState.BackedUp => "پشتیبان‌گیری‌شده", OperationState.Applying => "در حال اعمال", OperationState.Verifying => "در حال بررسی",
        OperationState.Completed => "تکمیل‌شده", OperationState.PartiallyFailed => "بخشی ناموفق", OperationState.RollbackPending => "در انتظار بازیابی",
        OperationState.RollingBack => "در حال بازیابی", OperationState.RolledBack => "بازیابی‌شده", OperationState.RecoveryRequired => "نیازمند بازیابی",
        _ => state.ToString()
    };
    private static string TranslateOperationState(string state) => Enum.TryParse<OperationState>(state, true, out var parsed) ? TranslateOperationState(parsed) : state;
    private string TranslateClassification(string value) => _persian ? value switch { "System critical" => "حیاتی سیستم", "Security / anti-cheat" => "امنیتی / ضدتقلب", "Windows / service" => "ویندوز / سرویس", _ => "برنامه کاربر" } : value;
    private void EnableDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        // Caption matches the window background so the title bar blends into the dark theme (Windows 11; ignored on older builds). COLORREF is 0x00BBGGRR.
        var caption = 0x000A0707;
        var text = 0x00FFFFFF;
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public sealed record LabelValue(string Label, string Value);
    public sealed record NetworkRow(string Name, string Summary);
    public sealed record StartupRow(string Name, string Source, string Command);
    public sealed record ProcessRow(int Id, string Name, string Memory, string CpuTime, string Classification);
    public sealed class BackupViewModel(string directory, DateTime timestamp, string state, int operationCount)
    {
        public string Directory { get; } = directory; public string Title { get; } = timestamp.ToString("yyyy-MM-dd  HH:mm:ss");
        public string State { get; } = state; public int OperationCount { get; } = operationCount;
        public string Subtitle => $"{State} • {OperationCount} operation(s)";
    }

    public sealed class RecommendationViewModel : INotifyPropertyChanged
    {
        private bool _selected;
        private readonly bool _persian;
        public RecommendationViewModel(PlannedTweak plan, TweakMetadata metadata, bool persian) { Plan = plan; Metadata = metadata; _selected = plan.Selected; _persian = persian; }
        public PlannedTweak Plan { get; } public TweakMetadata Metadata { get; }
        public string Name => UiLocalization.Translate(Plan.Name, _persian);
        public string Description => UiLocalization.Translate(Metadata.Description, _persian);
        public string Why => UiLocalization.Translate(Metadata.Why, _persian);
        public string Summary => Plan.Current.Status == DetectionStatus.Compliant ? UiLocalization.Translate("Already in the recommended state.", _persian) : Description;
        public string Risk => _persian ? $"ریسک {(int)Plan.Risk.Level} • {TranslateBenefit(Plan.Benefit)}" : $"RISK {(int)Plan.Risk.Level} • {Plan.Benefit}";
        public string Evidence => _persian ? TranslateEvidence(Metadata.Evidence) : Metadata.Evidence.ToString();
        public string Confidence => _persian ? TranslateConfidence(Metadata.Risk.Confidence) : Metadata.Risk.Confidence.ToString();
        public string Benefit => _persian ? TranslateBenefit(Metadata.Benefit) : Metadata.Benefit.ToString();
        public string Restore => _persian ? "بازگردانی دقیق مقدار ثبت‌شده پیش از تغییر" : Metadata.RollbackMethod;
        public string Compatibility => _persian ? (Plan.Compatibility.Status == CompatibilityStatus.Compatible ? "سازگار" : "ناسازگار") : Plan.Compatibility.Status.ToString();
        public bool CanSelect => Plan.Compatibility.Status == CompatibilityStatus.Compatible && Plan.Current.Status != DetectionStatus.Compliant;
        public bool IsSelected { get => _selected; set { if (_selected == value || !CanSelect) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private static string TranslateBenefit(BenefitLevel value) => value switch { BenefitLevel.None => "بدون اثر", BenefitLevel.Negligible => "ناچیز", BenefitLevel.Low => "کم", BenefitLevel.Moderate => "متوسط", BenefitLevel.PotentiallyHigh => "بالقوه زیاد", _ => "نامشخص" };
        private static string TranslateEvidence(EvidenceType value) => value switch { EvidenceType.OfficialDocumentation => "مستندات رسمی", EvidenceType.VendorDocumentation => "مستندات سازنده", EvidenceType.GenerallySupportedBehavior => "رفتار عمومی پشتیبانی‌شده", EvidenceType.HardwareDependent => "وابسته به سخت‌افزار", EvidenceType.Experimental => "آزمایشی", EvidenceType.Legacy => "تنظیم قدیمی/سازگاری", EvidenceType.ControlledBenchmark => "بنچمارک کنترل‌شده", EvidenceType.MeasuredOnThisMachine => "اندازه‌گیری‌شده روی این سیستم", _ => "نامشخص" };
        private static string TranslateConfidence(ProSyS.Core.Confidence value) => value switch { ProSyS.Core.Confidence.Verified => "تأییدشده", ProSyS.Core.Confidence.High => "زیاد", ProSyS.Core.Confidence.Medium => "متوسط", ProSyS.Core.Confidence.Experimental => "آزمایشی", _ => "نامشخص" };
    }
}
