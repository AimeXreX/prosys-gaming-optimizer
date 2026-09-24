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

namespace ProSyS.App;

public partial class MainWindow : Window
{
    private readonly WindowsSystemScanner _scanner = new();
    private readonly IReadOnlyList<ITweak> _tweaks = TweakCatalog.CreateSafeTweaks();
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProSySOptimizer");
    private readonly ObservableCollection<RecommendationViewModel> _recommendations = new();
    private readonly ObservableCollection<BackupViewModel> _backups = new();
    private readonly ObservableCollection<GameProfile> _gameProfiles = new();
    private readonly ObservableCollection<BenchmarkSummary> _benchmarkHistory = new();
    private readonly UiLocalization _localization = new();
    private readonly CheckBox _profileCpuPriorityCheck = new() { IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _cs2CpuBoostButton = new() { Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
    private CpuOptimizationSession? _activeCs2CpuSession;
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
        DataPathText.Text = _root;
        RecommendationsList.ItemsSource = _recommendations;
        BackupsList.ItemsSource = _backups;
        GameProfilesList.ItemsSource = _gameProfiles;
        BenchmarkHistoryList.ItemsSource = _benchmarkHistory;
        RecommendationFilter.Foreground = new SolidColorBrush(Color.FromRgb(32, 28, 42));
        if (ProfileOverlayCheck.Parent is Panel profileOptions)
        {
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _profileCpuPriorityCheck);
            _cs2CpuBoostButton.Style = (Style)FindResource("SecondaryButton");
            _cs2CpuBoostButton.Click += Cs2CpuBoost_Click;
            profileOptions.Children.Insert(profileOptions.Children.IndexOf(ProfileOverlayCheck), _cs2CpuBoostButton);
        }
        foreach (var category in _tweaks.Select(x => x.Metadata.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x))
            RecommendationFilter.Items.Add(new ComboBoxItem { Content = category, Tag = "category" });
        RefreshHistory();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _overlayWindow?.Close();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        foreach (var profile in await _profileStore.LoadAsync()) _gameProfiles.Add(profile);
        await LoadBenchmarkHistoryAsync();
        var trust = CreateBenchmarkEngine().VerifyTool();
        PresentMonTrustText.Text = trust.Trusted
            ? T("Verified: official Intel-signed PresentMon 2.6.0 with pinned SHA-256.", "تأیید شد: PresentMon رسمی نسخه ۲.۶.۰ با امضای Intel و هش ثابت.")
            : T(trust.Message, "ابزار بنچمارک قابل اعتماد نیست: " + trust.Message);
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
            RenderSnapshot(_snapshot);
            RenderRecommendations(_plan);
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
        AvailableValue.Text = _plan?.Tweaks.Count(x => x.Current.Status != DetectionStatus.Disabled).ToString() ?? "0";
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
        DetailEvidence.Text = _persian ? $"شواهد: {item.Evidence} • اطمینان: {item.Confidence} • اثر احتمالی: {item.Benefit}" : $"Evidence: {item.Evidence} • Confidence: {item.Confidence} • Benefit: {item.Benefit}";
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
            StatusSubtitle.Text = T($"{result.Results.Count(x => x.Success)} verified • Recovery data saved locally.", $"{result.Results.Count(x => x.Success)} تغییر تأیید شد • داده بازیابی به‌صورت محلی ذخیره شد.");
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
            var result = await Engine().RollbackAsync(item.Directory, _tweaks);
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
        _profileCpuPriorityCheck.Content = T("Prioritize CPU scheduling for CPU-bound games (High, never Realtime)", "اولویت‌دهی پردازنده برای بازی‌های CPU محور (High؛ هرگز Realtime)");
        _cs2CpuBoostButton.Content = T("ATTACH CPU BOOST TO RUNNING CS2", "اتصال تقویت پردازنده به CS2 در حال اجرا");
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
            _benchmarkHistory.Insert(0, result);
            await SaveBenchmarkHistoryAsync();
            RefreshBenchmarkDisplay();
        });
    }

    private void CompareBenchmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_benchmarkHistory.Count < 2) { MessageBox.Show(T("Capture at least two runs first.", "ابتدا دست‌کم دو اجرای بنچمارک ثبت کنید."), "ProSyS"); return; }
        var comparison = BenchmarkEngine.Compare(_benchmarkHistory[1], _benchmarkHistory[0]);
        BenchmarkResultText.Text = _persian
            ? $"مقایسه دو اجرای آخر\nمیانگین FPS: {comparison.AverageFpsDeltaPercent:+0.0;-0.0;0}%\nیک درصد پایین: {comparison.OnePercentLowDeltaPercent:+0.0;-0.0;0}%\nP99 زمان فریم: {comparison.P99FrameTimeDeltaPercent:+0.0;-0.0;0}%\nاعتبار مقایسه: {comparison.Comparability}\n{comparison.Reason}"
            : $"LAST TWO RUNS\nAverage FPS: {comparison.AverageFpsDeltaPercent:+0.0;-0.0;0}%\n1% low: {comparison.OnePercentLowDeltaPercent:+0.0;-0.0;0}%\nP99 frame time: {comparison.P99FrameTimeDeltaPercent:+0.0;-0.0;0}%\nComparability: {comparison.Comparability}\n{comparison.Reason}";
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
        catch (JsonException) { BenchmarkResultText.Text = T("Benchmark history is unreadable; captures were left untouched.", "تاریخچه بنچمارک خوانا نیست؛ فایل‌های ثبت‌شده دست‌نخورده باقی ماندند."); }
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
            OptimizationProfileKind.Safe => _tweaks.Where(x => x.Metadata.RecommendedByDefault).Select(x => x.Metadata.Id).ToArray(),
            OptimizationProfileKind.Balanced => _tweaks.Where(x => !x.Metadata.Category.Equals("Input", StringComparison.OrdinalIgnoreCase)).Select(x => x.Metadata.Id).ToArray(),
            _ => _tweaks.Select(x => x.Metadata.Id).ToArray()
        };
        var current = GameProfilesList.SelectedItem as GameProfile;
        var profile = new GameProfile(current?.Id ?? Guid.NewGuid(), name, process, path, kind, ids, ProfileRestoreCheck.IsChecked == true, ProfileOverlayCheck.IsChecked == true, DateTimeOffset.UtcNow, _profileCpuPriorityCheck.IsChecked == true);
        if (current is not null) _gameProfiles[_gameProfiles.IndexOf(current)] = profile; else _gameProfiles.Add(profile);
        await _profileStore.SaveAsync(_gameProfiles);
        GameProfilesList.SelectedItem = profile;
        GameSessionStatusText.Text = T("Profile saved locally.", "پروفایل به‌صورت محلی ذخیره شد.");
    }

    private void GameProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameProfilesList.SelectedItem is not GameProfile profile) return;
        ProfileNameText.Text = profile.Name; ProfileProcessText.Text = profile.GameProcess; ProfilePathText.Text = profile.ExecutablePath ?? string.Empty;
        ProfileKindBox.SelectedIndex = Math.Clamp((int)profile.Profile, 0, 2); ProfileRestoreCheck.IsChecked = profile.RestoreOnExit; ProfileOverlayCheck.IsChecked = profile.OverlayEnabled; _profileCpuPriorityCheck.IsChecked = profile.CpuPriorityEnabled;
    }

    private void Cs2CpuBoost_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCs2CpuSession is not null)
        {
            MessageBox.Show(T("CPU prioritization is already attached to CS2.", "اولویت‌دهی پردازنده از قبل به CS2 متصل است."), "ProSyS");
            return;
        }
        var game = Process.GetProcessesByName("cs2").FirstOrDefault(x => !x.HasExited);
        if (game is null)
        {
            MessageBox.Show(T("CS2 is not running. Start the game, enter the main menu, then try again.", "CS2 در حال اجرا نیست. بازی را اجرا کنید، وارد منوی اصلی شوید و دوباره تلاش کنید."), "ProSyS");
            return;
        }
        try
        {
            _activeCs2CpuSession = new ProcessCpuOptimizer().Apply(game);
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                _activeCs2CpuSession = null;
                GameSessionStatusText.Text = T("CS2 exited; the per-process CPU policy ended automatically.", "CS2 بسته شد؛ سیاست پردازنده مخصوص همان پردازش خودکار پایان یافت.");
            });
            GameSessionStatusText.Text = T($"CS2 CPU mode attached to PID {game.Id}: High priority + Windows priority boost. Realtime and forced 90% load are intentionally not used.",
                $"حالت پردازنده CS2 به پردازش {game.Id} متصل شد: اولویت High و Priority Boost ویندوز فعال است. حالت خطرناک Realtime و بار مصنوعی ۹۰٪ عمداً استفاده نمی‌شوند.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _activeCs2CpuSession = null;
            MessageBox.Show(T("Windows did not allow changing the CS2 process priority: ", "ویندوز اجازه تغییر اولویت پردازش CS2 را نداد: ") + ex.Message, "ProSyS", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var basePlan = await new PlanFactory().CreateAsync(snapshot, _tweaks);
            var plan = new PlanFactory().Select(basePlan, profile.TweakIds.ToHashSet(StringComparer.OrdinalIgnoreCase));
            var result = await Engine().ExecuteAsync(plan, _tweaks);
            if (result.State != OperationState.Completed) throw new InvalidOperationException(T("The game was not started because profile changes could not be verified.", "بازی اجرا نشد چون تغییرات پروفایل تأیید نشدند."));
            var game = runningGame ?? Process.Start(new ProcessStartInfo(profile.ExecutablePath!) { UseShellExecute = true }) ?? throw new InvalidOperationException(T("Game process could not be started.", "پردازش بازی اجرا نشد."));
            CpuOptimizationSession? cpuSession = null;
            if (profile.CpuPriorityEnabled)
            {
                cpuSession = new ProcessCpuOptimizer().Apply(game);
                var cpu = cpuSession.SampleCpuPercent();
                GameSessionStatusText.Text = T($"{profile.Name} attached (PID {game.Id}). CPU priority: High; current normalized CPU: {cpu:F0}%.",
                    $"{profile.Name} متصل شد (PID {game.Id}). اولویت پردازنده: High؛ مصرف فعلی نرمال‌شده: {cpu:F0}٪.");
            }
            if (profile.OverlayEnabled) ShowOverlay();
            if (!profile.CpuPriorityEnabled) GameSessionStatusText.Text = T($"{profile.Name} is running. {result.Results.Count} changes verified.", $"{profile.Name} در حال اجراست؛ {result.Results.Count} تغییر تأیید شد.");
            if (profile.RestoreOnExit)
            {
                game.EnableRaisingEvents = true;
                game.Exited += async (_, _) => await Dispatcher.InvokeAsync(async () =>
                {
                    var cpuRestored = cpuSession?.TryRestore() ?? true;
                    var rollback = await Engine().RollbackAsync(result.BackupDirectory, _tweaks);
                    GameSessionStatusText.Text = T($"Game exited. Original state restored: {rollback.Results.Count(x => x.Success)}/{rollback.Results.Count}; CPU policy restored: {cpuRestored}.", $"بازی بسته شد؛ وضعیت اصلی بازگردانده شد: {rollback.Results.Count(x => x.Success)}/{rollback.Results.Count}؛ سیاست پردازنده بازیابی شد: {(cpuRestored ? "بله" : "خیر")}.");
                    HideOverlay(); RefreshHistory();
                });
            }
        });
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
            RestorePointStatusText.Text = process.ExitCode == 0 ? T("Windows restore point created successfully.", "نقطه بازیابی ویندوز با موفقیت ساخته شد.") : T($"Restore point failed (code {process.ExitCode}).", $"ساخت نقطه بازیابی ناموفق بود (کد {process.ExitCode}).");
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
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
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
        public string Summary => Plan.Current.Status == DetectionStatus.Disabled ? UiLocalization.Translate("Already in the recommended state.", _persian) : Description;
        public string Risk => _persian ? $"ریسک {(int)Plan.Risk.Level} • {TranslateBenefit(Plan.Benefit)}" : $"RISK {(int)Plan.Risk.Level} • {Plan.Benefit}";
        public string Evidence => _persian ? TranslateEvidence(Metadata.Evidence) : Metadata.Evidence.ToString();
        public string Confidence => _persian ? TranslateConfidence(Metadata.Risk.Confidence) : Metadata.Risk.Confidence.ToString();
        public string Benefit => _persian ? TranslateBenefit(Metadata.Benefit) : Metadata.Benefit.ToString();
        public string Restore => _persian ? "بازگردانی دقیق مقدار ثبت‌شده پیش از تغییر" : Metadata.RollbackMethod;
        public string Compatibility => _persian ? (Plan.Compatibility.Status == CompatibilityStatus.Compatible ? "سازگار" : "ناسازگار") : Plan.Compatibility.Status.ToString();
        public bool CanSelect => Plan.Compatibility.Status == CompatibilityStatus.Compatible && Plan.Current.Status != DetectionStatus.Disabled;
        public bool IsSelected { get => _selected; set { if (_selected == value || !CanSelect) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private static string TranslateBenefit(BenefitLevel value) => value switch { BenefitLevel.None => "بدون اثر", BenefitLevel.Negligible => "ناچیز", BenefitLevel.Low => "کم", BenefitLevel.Moderate => "متوسط", BenefitLevel.PotentiallyHigh => "بالقوه زیاد", _ => "نامشخص" };
        private static string TranslateEvidence(EvidenceType value) => value switch { EvidenceType.OfficialDocumentation => "مستندات رسمی", EvidenceType.VendorDocumentation => "مستندات سازنده", EvidenceType.GenerallySupportedBehavior => "رفتار عمومی پشتیبانی‌شده", EvidenceType.HardwareDependent => "وابسته به سخت‌افزار", EvidenceType.Experimental => "آزمایشی", EvidenceType.Legacy => "تنظیم قدیمی/سازگاری", EvidenceType.ControlledBenchmark => "بنچمارک کنترل‌شده", EvidenceType.MeasuredOnThisMachine => "اندازه‌گیری‌شده روی این سیستم", _ => "نامشخص" };
        private static string TranslateConfidence(ProSyS.Core.Confidence value) => value switch { ProSyS.Core.Confidence.Verified => "تأییدشده", ProSyS.Core.Confidence.High => "زیاد", ProSyS.Core.Confidence.Medium => "متوسط", ProSyS.Core.Confidence.Experimental => "آزمایشی", _ => "نامشخص" };
    }
}
