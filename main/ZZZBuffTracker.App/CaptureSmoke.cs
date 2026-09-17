using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Capture;
using WinRT;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;
using ZZZBuffTracker.Domain;
using ZZZBuffTracker.Infrastructure;

namespace ZZZBuffTracker.App;

/// <summary>Opt-in integration diagnostic: only captures its own synthetic window.</summary>
internal static class CaptureSmoke
{
    public static async Task<int> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var clock = new MonotonicClock();
        var window = new Window { Title = "ZZZ Capture Smoke Target", Width = 320, Height = 320,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false };
        var pixels = Enumerable.Range(0, 64 * 64).Select(i => (byte)((i / 64 / 8 + i % 64 / 8) % 2 == 0 ? 240 : 16)).ToArray();
        var image = new Image { Source = CaptureWindow.ToBitmap(new(0, TimeSpan.Zero, 64, 64, pixels)), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var heartbeat = new TextBlock { Foreground = Brushes.Red, Background = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new Grid(); grid.Children.Add(image); grid.Children.Add(heartbeat); window.Content = grid;
        var ticks = 0;
        var animation = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Normal,
            (_, _) => heartbeat.Text = (++ticks).ToString(), window.Dispatcher);
        animation.Start();
        try
        {
            window.Show();
            await Task.Delay(300);
            var item = ForOwnWindow(new WindowInteropHelper(window).Handle);
            var source = new WindowsCaptureSource(item, clock);
            List<GrayFrame> frames = [];
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await foreach (var frame in source.ReadFramesAsync(timeout.Token))
                {
                    frame.Validate(); frames.Add(frame);
                    if (frames.Count == 5) break;
                }
            }
            var first = frames[0];
            if (first.Pixels.Max() - first.Pixels.Min() < 150) throw new InvalidOperationException("WGC returned a flat/black image.");
            if (frames.Zip(frames.Skip(1)).Any(pair => pair.Second.CapturedAt <= pair.First.CapturedAt))
                throw new InvalidOperationException("WGC timestamps did not increase.");
            var roi = new NormalizedRect(.25, .25, .5, .5);
            var crop = first.Crop(roi);
            var templatePixels = new byte[32 * 32];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                templatePixels[y * 32 + x] = crop.Pixels[(int)((y + .5) * crop.Height / 32) * crop.Width + (int)((x + .5) * crop.Width / 32)];
            var pack = new TemplatePack(1, "wgc-smoke-synthetic", 1, first.Width, first.Height, 1, NormalizedRect.Full,
                [new("checker", "demo-buff", TemplateKind.Buff, 1, roi, 32, 32, templatePixels)]);
            var detector = new TemplateDetector(pack);
            var context = new TrackingContext(Guid.NewGuid(), new CharacterPreset("demo-character", 1, []));
            var events = frames.SelectMany(f => detector.Process(f, context, clock.Elapsed).Events).ToArray();
            if (events.Length != 1 || events[0].Type != GameEventType.BuffIconAppeared)
                throw new InvalidOperationException("Captured checker did not produce exactly one appearance.");
            await ReplayDataset.SaveAsync(Path.Combine(directory, "dataset"), pack, frames);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(CaptureWindow.ToBitmap(first)));
            using (var file = File.Create(Path.Combine(directory, "capture.png"))) png.Save(file);
            bool resizeRejected = false, closedRejected = false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                try
                {
                    await foreach (var _ in source.ReadFramesAsync(timeout.Token)) window.Width = 400;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("size changed", StringComparison.Ordinal)) { resizeRejected = true; }
            }
            if (!resizeRejected) throw new InvalidOperationException("Resize was not detected.");
            source = new WindowsCaptureSource(ForOwnWindow(new WindowInteropHelper(window).Handle), clock);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                try
                {
                    await foreach (var _ in source.ReadFramesAsync(timeout.Token)) window.Close();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.Ordinal)) { closedRejected = true; }
            }
            if (!closedRejected) throw new InvalidOperationException("Closed target was not detected.");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
            { Passed = true, Frames = frames.Count, first.Width, first.Height, Events = events.Length, ResizeRejected = resizeRejected, ClosedRejected = closedRejected }, TemplateFiles.JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }, TemplateFiles.JsonOptions));
            return 1;
        }
        finally { animation.Stop(); window.Close(); }
    }

    // IGraphicsCaptureItemInterop is only used for the diagnostic's own HWND; normal UI uses the OS picker.
    private static GraphicsCaptureItem ForOwnWindow(nint hwnd)
    {
        nint text = 0, factory = 0, item = 0;
        try
        {
            const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out text));
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, in interopId, out factory));
            var vtable = Marshal.ReadIntPtr(factory);
            var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(vtable, 3 * nint.Size));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            Marshal.ThrowExceptionForHR(create(factory, hwnd, in itemId, out item));
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            if (text != 0) WindowsDeleteString(text);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindow(nint self, nint window, in Guid iid, out nint result);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
}
