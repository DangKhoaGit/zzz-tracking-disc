using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.App;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class OverlayItemViewModel(OverlayItem item) : ObservableModel
{
    public OverlayItem Value { get; private set; } = item;
    public void Update(OverlayItem next) { if (Value == next) return; Value = next; Changed(nameof(Value)); }
}

public sealed class OverlayViewModel : ObservableModel
{
    private readonly OverlaySettingsService settingsService;
    private OverlaySettings settings;
    private OverlaySnapshot latest = OverlaySnapshot.Empty;
    private bool visible = true;
    private bool editing;
    private bool previewSamples;
    private string message = "Ctrl+Alt+O: ẩn/hiện · Ctrl+Alt+E: sửa · Ctrl+Alt+R: reset buff";
    public ObservableCollection<OverlayItemViewModel> Items { get; } = [];
    public Array DisplayModes { get; } = Enum.GetValues<OverlayDisplayMode>();
    public Array Orientations { get; } = Enum.GetValues<OverlayOrientation>();
    public ICommand ToggleVisibleCommand { get; }
    public ICommand ToggleEditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RecoverPositionCommand { get; }
    public event Action? RecoverPositionRequested;

    public OverlayViewModel(OverlaySettings settings, OverlaySettingsService settingsService)
    {
        this.settings = settings.Validate();
        this.settingsService = settingsService;
        ToggleVisibleCommand = new AsyncCommand(() => { IsVisible = !IsVisible; return Task.CompletedTask; }, ReportError);
        ToggleEditCommand = new AsyncCommand(async () =>
        {
            if (IsEditing) await SaveAndPlayAsync();
            else { IsVisible = true; IsEditing = true; }
        }, ReportError);
        SaveCommand = new AsyncCommand(SaveAndPlayAsync, ReportError);
        RecoverPositionCommand = new AsyncCommand(() =>
        { IsVisible = true; IsEditing = true; RecoverPositionRequested?.Invoke(); return Task.CompletedTask; }, ReportError);
    }

    public bool IsVisible { get => visible; set { visible = value; Changed(); Changed(nameof(VisibilityLabel)); } }
    public bool IsEditing { get => editing; private set { editing = value; Changed(); Changed(nameof(ModeLabel)); } }
    public string VisibilityLabel => IsVisible ? "Ẩn overlay" : "Hiện overlay";
    public string ModeLabel => IsEditing ? "EDIT · kéo thanh tiêu đề / góc dưới để resize" : "PLAY · click-through";
    public string Message { get => message; private set { message = value; Changed(); } }
    public OverlayGeometry Geometry => settings.Geometry;
    public OverlaySettings ExportSettings => settings;
    public void ApplySettings(OverlaySettings value)
    {
        value.Validate();
        DisplayMode = value.DisplayMode; Orientation = value.Orientation; Scale = value.Scale;
        Opacity = value.Opacity; Spacing = value.Spacing; HideInactive = value.HideInactive;
        // Import visual preferences only; keep the current monitor-safe placement.
    }
    public OverlayDisplayMode DisplayMode
    {
        get => settings.DisplayMode;
        set { settings = settings with { DisplayMode = value }; Changed(); Changed(nameof(ShowIcon)); Changed(nameof(ShowName)); }
    }
    public bool ShowIcon => DisplayMode != OverlayDisplayMode.Text;
    public bool ShowName => DisplayMode != OverlayDisplayMode.Image;
    public OverlayOrientation Orientation
    { get => settings.Orientation; set { settings = settings with { Orientation = value }; Changed(); Changed(nameof(IsHorizontal)); } }
    public bool IsHorizontal => Orientation == OverlayOrientation.Horizontal;
    public double Scale
    { get => settings.Scale; set { if (!double.IsFinite(value)) return; settings = settings with { Scale = Math.Clamp(value, 0.5, 2) }; Changed(); } }
    public double Opacity
    { get => settings.Opacity; set { if (!double.IsFinite(value)) return; settings = settings with { Opacity = Math.Clamp(value, 0.2, 1) }; Changed(); } }
    public double Spacing
    { get => settings.Spacing; set { if (!double.IsFinite(value)) return; settings = settings with { Spacing = Math.Clamp(value, 0, 32) }; Changed(); } }
    public bool HideInactive
    { get => settings.HideInactive; set { settings = settings with { HideInactive = value }; Changed(); RefreshItems(); } }
    // Explicit demonstration of uncertain/multiple snapshots; never changes tracker state.
    public bool PreviewSamples
    { get => previewSamples; set { previewSamples = value; Changed(); RefreshItems(); } }

    public void Update(OverlaySnapshot snapshot) { latest = snapshot; RefreshItems(); }
    public void SetGeometry(OverlayGeometry geometry) => settings = settings with { Geometry = geometry };
    public void ReportError(Exception ex) => Message = ex.Message;
    public void SetMessage(string text) => Message = text;

    public Task SaveAsync() => settingsService.SaveAsync(settings);
    private async Task SaveAndPlayAsync()
    {
        await SaveAsync();
        IsEditing = false;
        Message = "Đã lưu overlay. Play Mode không chặn chuột.";
    }

    private void RefreshItems()
    {
        var snapshot = PreviewSamples ? new OverlaySnapshot(Guid.Empty, TrackingStatus.Manual,
            [new("sample-a", "DEMO · Active", BuffStatus.Active, 4.8, 0.8, 2),
             new("sample-b", "DEMO · Unknown", BuffStatus.Unknown, 0, 0, 1),
             new("sample-c", "DEMO · Inactive", BuffStatus.Inactive, 0, 0, 0)], 0) : latest;
        var projected = OverlayPresentation.Project(snapshot, HideInactive);
        // Preserve item instances during timer ticks so WPF does not rebuild the visual tree.
        if (Items.Count != projected.Length || !Items.Select(x => x.Value.Id).SequenceEqual(projected.Select(x => x.Id)))
        {
            Items.Clear();
            foreach (var item in projected) Items.Add(new(item));
        }
        else for (var i = 0; i < Items.Count; i++) Items[i].Update(projected[i]);
    }
}
