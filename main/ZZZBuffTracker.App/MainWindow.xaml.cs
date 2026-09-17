using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;

namespace ZZZBuffTracker.App;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel viewModel;
    private bool closing;
    private bool shutdownComplete;
    private readonly OverlayWindow overlayWindow;
    private GlobalHotkeys? hotkeys;
    private ProfileWindow? profileWindow;
    private CaptureWindow? captureWindow;
    private readonly CaptureEventSource captureSource;
    private readonly IClock clock;
    private void OpenCapture(object sender, RoutedEventArgs e)
    {
        if (captureWindow is null)
        {
            captureWindow = new CaptureWindow(captureSource, clock, () => viewModel.StartCommand.Execute(null)) { Owner = this };
            captureWindow.Closed += (_, _) => captureWindow = null;
        }
        captureWindow.Show(); captureWindow.Activate();
    }
    private void OpenProfiles(object sender, RoutedEventArgs e)
    {
        if (profileWindow is null)
        {
            profileWindow = new ProfileWindow(viewModel.Profiles) { Owner = this };
            profileWindow.Closed += (_, _) => profileWindow = null;
        }
        profileWindow.Show(); profileWindow.Activate();
    }

    public MainWindow(DashboardViewModel viewModel, CaptureEventSource captureSource, IClock clock)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.captureSource = captureSource;
        this.clock = clock;
        DataContext = viewModel;
        overlayWindow = new OverlayWindow(viewModel.Overlay);
        SourceInitialized += (_, _) => RegisterHotkeys();
        Loaded += (_, _) => overlayWindow.Show();
        Closing += OnClosing;
    }

    private void RegisterHotkeys()
    {
        hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle);
        var failed = new List<string>();
        if (!hotkeys.Register(1, 0x4F, () => viewModel.Overlay.ToggleVisibleCommand.Execute(null))) failed.Add("Ctrl+Alt+O");
        if (!hotkeys.Register(2, 0x45, () => viewModel.Overlay.ToggleEditCommand.Execute(null))) failed.Add("Ctrl+Alt+E");
        if (!hotkeys.Register(3, 0x52, () => viewModel.ResetCommand.Execute(null))) failed.Add("Ctrl+Alt+R");
        if (!hotkeys.Register(4, 0x54, () => viewModel.TriggerCommand.Execute(null))) failed.Add("Ctrl+Alt+T");
        if (!hotkeys.Register(5, 0x46, () => viewModel.RefreshBuffCommand.Execute(null))) failed.Add("Ctrl+Alt+F");
        if (!hotkeys.Register(6, 0x44, () => viewModel.ToggleDetectionCommand.Execute(null))) failed.Add("Ctrl+Alt+D");
        if (failed.Count > 0) viewModel.Overlay.SetMessage("Không đăng ký được hotkey: " + string.Join(", ", failed) + ". Có thể phím đang được ứng dụng khác dùng; hãy dùng nút trong Control Panel.");
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        hotkeys?.Dispose();
        overlayWindow.Shutdown();
        try { await viewModel.DisposeAsync(); }
        finally
        {
            shutdownComplete = true;
            // Dispose can complete synchronously when tracking never started. Closing again
            // inside the original Closing event is reentrant; defer it to the next UI turn.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
