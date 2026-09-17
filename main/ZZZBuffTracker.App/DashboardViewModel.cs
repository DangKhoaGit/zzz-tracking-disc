using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.App;

public sealed class DashboardViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly TrackingService service;
    private readonly DispatcherTimer timer;
    private OverlaySnapshot snapshot = OverlaySnapshot.Empty;
    private string message = "Sẵn sàng. Bấm Bắt đầu, sau đó kích hoạt buff demo.";
    public static CharacterPreset DemoPreset { get; } = new("demo-character", 1,
        [new("demo-buff", "Buff minh họa · 6 giây", TimeSpan.FromSeconds(6), MaxStacks: 3,
            SignalLoss: SignalLossPolicy.KeepUnknown)]);

    private readonly CaptureEventSource source;
    public DashboardViewModel(TrackingService service, CaptureEventSource source, string logPath, OverlayViewModel overlay, ProfileViewModel profiles)
    {
        this.service = service;
        this.source = source;
        Overlay = overlay;
        Profiles = profiles;
        LogPath = logPath;
        StartCommand = new AsyncCommand(() => service.StartAsync(Profiles.SelectedPreset
            ?? throw new InvalidOperationException("Chọn hoặc tạo preset trong Presets trước khi Start.")), ShowError);
        StopCommand = new AsyncCommand(service.StopAsync, ShowError);
        TriggerCommand = new AsyncCommand(() =>
        {
            Message = service.SubmitManual(GameEventType.ManualTrigger, TargetBuff()) ? "Đã gửi ManualTrigger." : "Chưa sẵn sàng hoặc hàng đợi đầy. Hãy thử lại.";
            return Task.CompletedTask;
        }, ShowError);
        ResetCommand = new AsyncCommand(() =>
        {
            Message = service.SubmitManual(GameEventType.Reset, "*") ? "Đã gửi Reset." : "Hãy bắt đầu tracking trước.";
            return Task.CompletedTask;
        }, ShowError);
        StackCommand = Simulate(GameEventType.ManualStack);
        RefreshBuffCommand = Simulate(GameEventType.ManualRefresh);
        PendingCommand = Simulate(GameEventType.BuffCandidate);
        SignalLostCommand = Simulate(GameEventType.SignalLost);
        DeactivateCommand = Simulate(GameEventType.ManualDeactivate);
        ExpireCommand = Simulate(GameEventType.ManualExpire);
        ToggleDetectionCommand = new AsyncCommand(() =>
        {
            service.SetDetectionEnabled(!service.DetectionEnabled); OnPropertyChanged(nameof(DetectionLabel));
            return Task.CompletedTask;
        }, ShowError);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        timer.Start();

        ICommand Simulate(GameEventType type) => new AsyncCommand(() =>
        {
            Message = service.SubmitManual(type, TargetBuff()) ? $"Đã gửi {type}." : "Hãy bắt đầu tracking hoặc thử lại khi hàng đợi trống.";
            return Task.CompletedTask;
        }, ShowError);
    }

    public ICommand StartCommand { get; }
    public OverlayViewModel Overlay { get; }
    public ProfileViewModel Profiles { get; }
    public ICommand ToggleDetectionCommand { get; }
    public string DetectionLabel => service.DetectionEnabled ? "Tạm dừng detection" : "Bật detection";
    public string CaptureSummary => $"{source.Diagnostics.Status}: {source.Diagnostics.Message}";
    private string TargetBuff() => service.Snapshot.Buffs.FirstOrDefault(b => b.Id == Profiles.SelectedBuff?.Id)?.Id
        ?? service.Snapshot.Buffs.FirstOrDefault()?.Id ?? throw new InvalidOperationException("Chưa có buff được cấu hình trong session hiện tại.");
    public ICommand StopCommand { get; }
    public ICommand TriggerCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand StackCommand { get; }
    public ICommand RefreshBuffCommand { get; }
    public ICommand PendingCommand { get; }
    public ICommand SignalLostCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ExpireCommand { get; }
    public string LogPath { get; }
    public ImmutableArray<BuffSnapshot> Buffs => snapshot.Buffs;
    public string Status => snapshot.Status switch
    { TrackingStatus.Manual => source.Diagnostics.Status == CaptureStatus.Capturing ? "CAPTURE / MANUAL FALLBACK" : "MANUAL / GIẢ LẬP", TrackingStatus.NotConfigured => "CHƯA CẤU HÌNH / NotConfigured", TrackingStatus.Faulted => "LỖI NGUỒN SỰ KIỆN", _ => "ĐÃ DỪNG" };
    public string Session => snapshot.SessionId == Guid.Empty ? "—" : snapshot.SessionId.ToString("N")[..8];
    public long ProcessedEvents => snapshot.ProcessedEvents;
    public string RuleDiagnostics => $"Character: {snapshot.CharacterId ?? "—"} · Rejected: {snapshot.RejectedEvents} · {snapshot.LastDecision ?? "—"}";
    public string Message { get => message; private set { message = value; OnPropertyChanged(); } }

    private void Refresh()
    {
        snapshot = service.Snapshot;
        Overlay.Update(snapshot);
        OnPropertyChanged(nameof(Buffs)); OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Session)); OnPropertyChanged(nameof(ProcessedEvents));
        OnPropertyChanged(nameof(RuleDiagnostics));
        OnPropertyChanged(nameof(CaptureSummary));
        if (snapshot.Error is { } error) Message = error;
    }

    private void ShowError(Exception ex) => Message = ex.Message;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        timer.Stop();
        await service.DisposeAsync();
    }
}

internal sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter) => !running;
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (Exception ex) { onError(ex); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
