using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.App;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel viewModel;
    private HwndSource? source;
    private bool shuttingDown;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        ApplyGeometry(viewModel.Geometry);
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessages);
            UpdateMode();
        };
        LocationChanged += (_, _) => CaptureGeometry();
        SizeChanged += (_, _) => CaptureGeometry();
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.RecoverPositionRequested += RecoverPosition;
        Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; viewModel.IsVisible = false; } };
    }

    private void ApplyGeometry(OverlayGeometry geometry)
    {
        Width = geometry.Width; Height = geometry.Height;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var saved = new Rect(geometry.Left, geometry.Top, geometry.Width, geometry.Height);
        var intersection = Rect.Intersect(desktop, saved);
        if (intersection.IsEmpty || intersection.Width < 80 || intersection.Height < 40)
        { Left = SystemParameters.WorkArea.Left + 24; Top = SystemParameters.WorkArea.Top + 24; }
        else { Left = geometry.Left; Top = geometry.Top; }
    }

    private void RecoverPosition()
    {
        Left = SystemParameters.WorkArea.Left + 24; Top = SystemParameters.WorkArea.Top + 24;
        Width = 380; Height = 300;
        CaptureGeometry();
    }

    private void CaptureGeometry()
    {
        if (double.IsFinite(Left) && double.IsFinite(Top) && ActualWidth >= MinWidth && ActualHeight >= MinHeight)
            viewModel.SetGeometry(new(Left, Top, ActualWidth, ActualHeight));
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsEditing)) UpdateMode();
        if (e.PropertyName == nameof(OverlayViewModel.IsVisible))
        {
            if (viewModel.IsVisible) { Show(); UpdateMode(); }
            else Hide();
        }
    }

    private void UpdateMode()
    {
        if (source is null) return;
        try { NativeOverlay.SetPlayMode(source.Handle, !viewModel.IsEditing); }
        catch (Win32Exception ex)
        {
            // Never leave a potentially input-blocking Play window on top of the game.
            viewModel.ReportError(ex); viewModel.IsVisible = false;
        }
    }

    private IntPtr WindowMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!viewModel.IsEditing && msg == 0x0021) { handled = true; return new(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    { if (viewModel.IsEditing && e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void ResizeOverlay(object sender, DragDeltaEventArgs e)
    {
        if (!viewModel.IsEditing) return;
        Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(ActualHeight + e.VerticalChange, MinHeight, MaxHeight);
    }
    public void Shutdown()
    {
        CaptureGeometry();
        shuttingDown = true;
        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.RecoverPositionRequested -= RecoverPosition;
        source?.RemoveHook(WindowMessages);
        Close();
    }
}
