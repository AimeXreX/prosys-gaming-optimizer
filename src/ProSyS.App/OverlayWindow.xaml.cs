using System.Windows;
using System.Windows.Threading;
using ProSyS.Windows;

namespace ProSyS.App;

public partial class OverlayWindow : Window
{
    private readonly LiveMetricsSampler _sampler = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Position();
        _timer.Tick += (_, _) => RefreshMetrics();
        _timer.Start();
    }
    protected override void OnClosed(EventArgs e) { _timer.Stop(); base.OnClosed(e); }
    private void Position() { Left = SystemParameters.WorkArea.Right - Width - 18; Top = SystemParameters.WorkArea.Top + 18; }
    private void RefreshMetrics()
    {
        var metrics = _sampler.Sample();
        CpuText.Text = $"{metrics.CpuPercent:F0}%";
        RamText.Text = $"{metrics.MemoryLoadPercent}%";
        NetworkText.Text = metrics.NetworkBytesPerSecond > 1048576 ? $"{metrics.NetworkBytesPerSecond / 1048576:F1} MB/s" : $"{metrics.NetworkBytesPerSecond / 1024:F0} KB/s";
    }
}
