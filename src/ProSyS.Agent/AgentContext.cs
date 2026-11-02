using System.Diagnostics;
using ProSyS.Core;
using ProSyS.Windows;
using ProSyS.Windows.GameSession;

namespace ProSyS.Agent;

/// <summary>
/// Tray-resident agent: detects a game, applies its profile (or safe auto levers), keeps background apps throttled while it runs,
/// and restores everything when the game exits or the agent is closed.
/// </summary>
public sealed class AgentContext : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProSySOptimizer");
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _autoItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _libraryItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _startupItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _openItem = new();
    private readonly ToolStripMenuItem _restoreItem = new();
    private readonly ToolStripMenuItem _languageItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly GameSessionManager _manager;
    private readonly GameProfileStore _profiles;
    private readonly GameDetector _detector = new(GameDetector.DiscoverLibraryRoots());
    private IReadOnlyList<GameProfile> _profileCache = Array.Empty<GameProfile>();
    private DateTime _profileStamp;
    private ActiveGameSession? _active;
    private AgentSettings _settings;
    private Task? _loop;

    public AgentContext(bool startedWithWindows)
    {
        Directory.CreateDirectory(_root);
        _settings = AgentSettings.Load(_root);
        _manager = new GameSessionManager(_root, TweakCatalog.CreateTweaks(), TweakCatalog.CreateRollbackCatalog(), new JsonAuditLog(Path.Combine(_root, "Logs", "audit.jsonl")));
        _profiles = new GameProfileStore(_root);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _autoItem.Checked = _settings.AutoOptimize;
        _libraryItem.Checked = _settings.AutoDetectLibraryGames;
        _startupItem.Checked = AgentAutostart.IsEnabled(Environment.ProcessPath ?? AgentAutostart.DefaultAgentPath);
        _autoItem.CheckedChanged += (_, _) => SaveSettings(_settings with { AutoOptimize = _autoItem.Checked });
        _libraryItem.CheckedChanged += (_, _) => SaveSettings(_settings with { AutoDetectLibraryGames = _libraryItem.Checked });
        _startupItem.CheckedChanged += (_, _) => AgentAutostart.Set(Environment.ProcessPath ?? AgentAutostart.DefaultAgentPath, _startupItem.Checked);
        _openItem.Click += (_, _) => OpenControlCenter();
        _restoreItem.Click += async (_, _) => await RestoreEverythingAsync();
        _languageItem.Click += (_, _) => { SaveSettings(_settings with { Persian = !_settings.Persian }); ApplyText(); };
        _exitItem.Click += async (_, _) => await ExitAsync();
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { _statusItem, new ToolStripSeparator(), _autoItem, _libraryItem, _startupItem, new ToolStripSeparator(), _openItem, _restoreItem, _languageItem, new ToolStripSeparator(), _exitItem });
        _tray = new NotifyIcon { Icon = LoadIcon(), ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => OpenControlCenter();
        ApplyText();
        _loop = Task.Run(() => RunAsync(startedWithWindows, _stop.Token));
    }

    private async Task RunAsync(bool startedWithWindows, CancellationToken ct)
    {
        var recovered = await _manager.RecoverAsync(ct);
        if (recovered.Count > 0) Notify(T("Recovered after an unexpected shutdown", "بازیابی پس از خاموشی ناگهانی"), string.Join("\n", recovered.Select(x => x.Text(_settings.Persian))));
        else if (!startedWithWindows) Notify("ProSyS", T("Watching for games. Optimizations apply automatically and are undone when the game closes.", "در انتظار اجرای بازی. بهینه‌سازی خودکار اعمال و پس از بستن بازی بازگردانده می‌شود."));
        using var timer = new PeriodicTimer(PollInterval);
        var tick = 0;
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (_active is { } session)
                {
                    session.Game.Refresh();
                    if (session.Game.HasExited) await EndSessionAsync(T("Game closed", "بازی بسته شد"));
                    else if (++tick % 10 == 0) await session.RefreshBackgroundAsync(ct);
                    continue;
                }
                if (!_settings.AutoOptimize) continue;
                var detected = _detector.Poll(await LoadProfilesAsync(ct), _settings.AutoDetectLibraryGames);
                if (detected is null) continue;
                _detector.Ignore(detected.Process.Id);
                var game = detected.Process;
                var started = await _manager.StartAsync(detected.Profile, WindowsSystemScanner.CaptureLightweight(),
                    detected.Profile.ExecutablePath ?? GameDetector.TryGetImagePath(game.Id), _ => Task.FromResult(game), gameAlreadyRunning: true, ct);
                if (started is null) { detected.Process.Dispose(); continue; }
                _active = started;
                tick = 0;
                var applied = started.StartOutcomes.Where(x => x.Status == LeverStatus.Applied).Select(x => "✓ " + x.Text(_settings.Persian));
                Notify(T($"{started.Profile.Name} optimized", $"{started.Profile.Name} بهینه شد") + (detected.AutoDetected ? T(" (auto)", " (خودکار)") : string.Empty),
                    string.Join("\n", applied.DefaultIfEmpty(T("Already optimal; nothing needed changing.", "همه‌چیز از قبل بهینه بود."))) +
                    (started.ThrottledCount > 0 ? "\n" + T($"✓ {started.ThrottledCount} background apps throttled", $"✓ {started.ThrottledCount} برنامه پس‌زمینه محدود شد") : string.Empty));
                UpdateStatus();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await new JsonAuditLog(Path.Combine(_root, "Logs", "audit.jsonl")).WriteAsync(new { operation = "agent-error", error = ex.Message }, CancellationToken.None);
            }
        }
    }

    private async Task EndSessionAsync(string title)
    {
        if (Interlocked.Exchange(ref _active, null) is not { } session) return;
        var outcomes = await session.EndAsync();
        session.Game.Dispose();
        var failed = outcomes.Count(x => x.Status == LeverStatus.Failed);
        Notify(title, failed == 0 ? T("Everything was restored to your original settings.", "همه تنظیمات به حالت اصلی شما برگشت.")
            : string.Join("\n", outcomes.Where(x => x.Status == LeverStatus.Failed).Select(x => "⚠ " + x.Text(_settings.Persian))));
        UpdateStatus();
    }

    private async Task<IReadOnlyList<GameProfile>> LoadProfilesAsync(CancellationToken ct)
    {
        var path = Path.Combine(_root, "game-profiles.json");
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (stamp != _profileStamp) { _profileCache = await _profiles.LoadAsync(ct); _profileStamp = stamp; }
        return _profileCache;
    }

    private async Task RestoreEverythingAsync()
    {
        await EndSessionAsync(T("Session ended", "نشست پایان یافت"));
        var recovered = await _manager.RecoverAsync();
        Notify(T("Restore complete", "بازگردانی کامل شد"), T("Session changes were restored. Per-game GPU choices can be undone in History & Backups.", "تغییرات نشست بازگردانده شد. انتخاب GPU مخصوص بازی‌ها را می‌توانید از تاریخچه و پشتیبان بازگردانید.") +
            (recovered.Count > 0 ? "\n" + string.Join("\n", recovered.Select(x => x.Text(_settings.Persian))) : string.Empty));
    }

    private async Task ExitAsync()
    {
        _stop.Cancel();
        try { if (_loop is not null) await _loop; } catch (OperationCanceledException) { }
        await EndSessionAsync(T("ProSyS agent closed", "عامل ProSyS بسته شد"));
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private void OpenControlCenter()
    {
        var app = Path.Combine(AppContext.BaseDirectory, "ProSyS.App.exe");
        if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true })?.Dispose();
        else Notify("ProSyS", T("ProSyS.App.exe was not found next to the agent.", "فایل ProSyS.App.exe کنار عامل پیدا نشد."));
    }

    private void SaveSettings(AgentSettings settings) { _settings = settings; settings.Save(_root); }

    private void ApplyText()
    {
        _autoItem.Text = T("Auto-optimize games", "بهینه‌سازی خودکار بازی‌ها");
        _libraryItem.Text = T("Detect games without a profile (Steam, Epic, Xbox…)", "تشخیص بازی‌های بدون پروفایل (Steam، Epic، Xbox…)");
        _startupItem.Text = T("Start with Windows", "اجرا همراه ویندوز");
        _openItem.Text = T("Open control center", "باز کردن مرکز کنترل");
        _restoreItem.Text = T("Restore everything now", "بازگردانی همه تغییرات");
        _languageItem.Text = _settings.Persian ? "English" : "فارسی";
        _exitItem.Text = T("Exit", "خروج");
        UpdateStatus();
    }

    private void UpdateStatus() => _ui.Post(_ =>
    {
        var text = _active is { } session ? T($"Optimizing: {session.Profile.Name}", $"در حال بهینه‌سازی: {session.Profile.Name}")
            : _settings.AutoOptimize ? T("Watching for games", "در انتظار اجرای بازی") : T("Paused", "متوقف");
        _statusItem.Text = text;
        _tray.Text = ("ProSyS — " + text)[..Math.Min(63, ("ProSyS — " + text).Length)];
    }, null);

    private void Notify(string title, string body)
    {
        if (!_settings.Notifications) return;
        _ui.Post(_ => _tray.ShowBalloonTip(5000, title, body.Length > 250 ? body[..250] : body, ToolTipIcon.None), null);
    }

    /// <summary>Loads the tray-sized frame of the brand icon so Windows does not downscale the 256 px frame.</summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(AgentContext).Assembly.GetManifestResourceStream("ProSyS.Agent.prosys.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private string T(string english, string persian) => _settings.Persian ? persian : english;
}
