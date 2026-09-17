using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Graphics.Capture;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;

namespace ZZZBuffTracker.App;

public partial class CaptureWindow : Window
{
    private readonly CaptureEventSource source;
    private readonly IClock clock;
    private readonly Action start;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource closed = new();
    private TemplatePack? pack;
    private IFrameSource? selectedSource;
    private GrayFrame? displayed;
    private bool recording;

    public CaptureWindow(CaptureEventSource source, IClock clock, Action start)
    {
        InitializeComponent(); this.source = source; this.clock = clock; this.start = start;
        pack = source.ConfiguredPack; selectedSource = source.ConfiguredSource;
        if (pack is not null) { ClientText.Text = RectText(pack.ClientArea); ScaleText.Text = pack.UiScale.ToString(CultureInfo.InvariantCulture); }
        UpdatePack();
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Closed += (_, _) => { timer.Stop(); closed.Cancel(); };
        timer.Start();
    }
    private void Refresh()
    {
        var d = source.Diagnostics;
        StatusText.Text = $"{d.Status}: {d.Message}\nFrames {d.Frames} · dropped {d.DroppedFrames} · pipeline {d.LatencyMilliseconds:F1} ms";
        if (d.Decisions.Length > 0) DecisionsText.Text = d.Decisions;
        if (FreezePreview.IsChecked != true && source.LatestFrame is { } frame && !ReferenceEquals(frame, displayed))
        {
            displayed = frame; FrameImage.Source = ToBitmap(frame);
            try { ShowRoi(); } catch (ArgumentException) { /* Incomplete text while editing; explicit Preview reports errors. */ }
            catch (FormatException) { }
        }
    }
    private async void PickCapture(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture không khả dụng.");
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, new WindowInteropHelper(this).Handle);
        var item = await picker.PickSingleItemAsync();
        if (item is null || closed.IsCancellationRequested) return;
        selectedSource = new WindowsCaptureSource(item, clock);
        source.Configure(selectedSource, pack); FreezePreview.IsChecked = false;
        MessageText.Text = $"Đã chọn {item.DisplayName}. Bấm Bắt đầu tracking. Chưa có pack thì chỉ preview.";
    });
    private void StartTracking(object sender, RoutedEventArgs e) => start();
    private void ManualOnly(object sender, RoutedEventArgs e) { selectedSource = null; source.Configure(null, null); }
    private async void LoadPack(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Template pack (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        pack = await TemplateFiles.LoadAsync(dialog.FileName, closed.Token);
        ClientText.Text = RectText(pack.ClientArea); ScaleText.Text = pack.UiScale.ToString(CultureInfo.InvariantCulture);
        UpdatePack(); source.Configure(selectedSource, pack);
    });
    private async void SavePack(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (pack is null) throw new InvalidOperationException("Tạo hoặc nạp pack trước.");
        var dialog = new SaveFileDialog { Filter = "Template pack (*.json)|*.json", FileName = $"{pack.Id}-v{pack.Version}.json" };
        if (dialog.ShowDialog(this) != true) return;
        await TemplateFiles.SaveNewAsync(dialog.FileName, pack, closed.Token);
        MessageText.Text = "Đã lưu pack. Nạp lại file này ở lần mở ứng dụng tiếp theo.";
    });
    private async void OpenScreenshot(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Screenshot (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        displayed = ReadScreenshot(dialog.FileName, clock.Elapsed);
        FreezePreview.IsChecked = true; FrameImage.Source = ToBitmap(displayed); ShowRoi();
        MessageText.Text = "Screenshot chỉ dùng hiệu chỉnh/Test score; không gửi event vào tracking.";
        return Task.CompletedTask;
    });
    private async void OpenReplay(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Replay manifest (replay.json)|replay.json|JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var loaded = await ReplayDataset.LoadAsync(dialog.FileName, closed.Token);
        pack = loaded.Pack; ClientText.Text = RectText(pack.ClientArea); UpdatePack();
        selectedSource = new ReplayFrameSource(dialog.FileName, clock);
        source.Configure(selectedSource, pack); FreezePreview.IsChecked = false;
        MessageText.Text = "Replay đã chọn. Bắt đầu tracking; replay phát event thật vào session hiện tại.";
    });
    private async void RecordReplay(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (recording) return;
        if (pack is null || source.Diagnostics.Status != CaptureStatus.Capturing) throw new InvalidOperationException("Cần pack và capture đang chạy để ghi replay.");
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục cha để lưu dataset mới" };
        if (dialog.ShowDialog(this) != true) return;
        recording = true;
        var recordedPack = pack;
        var recordedSource = source.ConfiguredSource;
        try
        {
            List<GrayFrame> frames = []; long bytes = 0;
            MessageText.Text = "Đang ghi tối đa 3 giây / 128 MB…";
            for (var i = 0; i < 30; i++)
            {
                closed.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(source.ConfiguredPack, recordedPack) || !ReferenceEquals(source.ConfiguredSource, recordedSource))
                    throw new InvalidOperationException("Nguồn hoặc pack đã đổi trong lúc ghi. Ghi lại replay với cấu hình ổn định.");
                if (source.LatestFrame is { } frame && (frames.Count == 0 || frame.CapturedAt > frames[^1].CapturedAt))
                {
                    if (bytes + frame.Pixels.Length > ReplayDataset.MaximumBytes) break;
                    frames.Add(frame); bytes += frame.Pixels.Length;
                }
                await Task.Delay(100, closed.Token);
            }
            var path = Path.Combine(dialog.FolderName, $"replay-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            await ReplayDataset.SaveAsync(path, recordedPack, frames, closed.Token);
            MessageText.Text = $"Đã lưu {frames.Count} frame tại {path}. Bổ sung nhãn Expected trong replay.json để đánh giá FP/FN.";
        }
        finally { recording = false; }
    });
    private async void EvaluateReplay(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Replay manifest|replay.json" };
        if (dialog.ShowDialog(this) != true) return;
        var character = CharacterText.Text.Trim();
        var report = await Task.Run(() => ReplayEvaluator.EvaluateAsync(dialog.FileName, character, closed.Token), closed.Token);
        MessageText.Text = $"Offline: {report.Frames} frames, {report.LabelledFrames} có nhãn; TP={report.TruePositives}, FP={report.FalsePositives}, FN={report.FalseNegatives}; xử lý={report.ProcessingMilliseconds:F1} ms. Không thay tracking.";
        DecisionsText.Text = string.Join("\n", report.Mismatches);
    });
    private async void PreviewRoi(object sender, RoutedEventArgs e) => await Run(() => { ShowRoi(); return Task.CompletedTask; });
    private async void AddTemplate(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var frame = displayed ?? throw new InvalidOperationException("Chưa có frame/screenshot.");
        var clientArea = ParseRect(ClientText.Text); var roi = ParseRect(RoiText.Text);
        var client = frame.Crop(clientArea); var icon = client.Crop(roi);
        // Fixed small template keeps matching bounded; use same nearest sampling as matcher.
        const int size = 32;
        var pixels = new byte[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            pixels[y * size + x] = icon.Pixels[Math.Min(icon.Height - 1, (int)((y + .5) * icon.Height / size)) * icon.Width
                + Math.Min(icon.Width - 1, (int)((x + .5) * icon.Width / size))];
        var kind = KindBox.SelectedIndex == 0 ? TemplateKind.Buff : TemplateKind.Character;
        var template = new DetectionTemplate(TemplateIdText.Text.Trim(), SubjectText.Text.Trim(), kind,
            (pack?.Templates.FirstOrDefault(t => t.Id == TemplateIdText.Text.Trim())?.Version ?? 0) + 1,
            roi, size, size, pixels, Number(OnText.Text), Number(OffText.Text), int.Parse(CountText.Text, CultureInfo.InvariantCulture),
            CharacterId: kind == TemplateKind.Buff && !string.IsNullOrWhiteSpace(CharacterText.Text) ? CharacterText.Text.Trim() : null);
        var scale = Number(ScaleText.Text);
        if (pack is not null && (pack.ClientArea != clientArea || pack.ReferenceWidth != client.Width
            || pack.ReferenceHeight != client.Height || pack.UiScale != scale))
            throw new InvalidOperationException("Geometry/UI scale khác pack hiện tại. Chọn Pack mới để hiệu chỉnh lại.");
        var templates = pack?.Templates.Where(t => t.Id != template.Id).ToImmutableArray() ?? [];
        var updated = new TemplatePack(1, pack?.Id ?? "local-icons", (pack?.Version ?? 0) + 1,
            client.Width, client.Height, scale, clientArea, templates.Add(template));
        updated.Validate(); pack = updated; UpdatePack(); source.Configure(selectedSource, pack);
        MessageText.Text = "Đã cập nhật template trong bộ nhớ; Lưu pack để dùng lại. Thử ảnh âm tính trước khi tin kết quả.";
        return Task.CompletedTask;
    });
    private async void TestFrame(object sender, RoutedEventArgs e) => await Run(() =>
    {
        if (pack is null || displayed is null) throw new InvalidOperationException("Cần pack và ảnh.");
        var client = displayed.Crop(pack.ClientArea);
        DecisionsText.Text = string.Join("\n", pack.Templates.Select(t => $"{t.Id}: score={TemplateDetector.Match(client, t):F4}; on={t.OnThreshold}; off={t.OffThreshold}"));
        MessageText.Text = "Test tĩnh: chỉ score, chưa xác nhận nhiều frame và không gửi event.";
        return Task.CompletedTask;
    });
    private void NewPack(object sender, RoutedEventArgs e) { pack = null; UpdatePack(); source.Configure(selectedSource, null); }
    private void UpdatePack() => PackText.Text = pack is null ? "Chưa có pack: chỉ preview."
        : $"{pack.Id} v{pack.Version} · {pack.ReferenceWidth}×{pack.ReferenceHeight} · UI scale {pack.UiScale} · "
            + string.Join(", ", pack.Templates.Select(t => $"{t.Id} → {t.SubjectId} ({t.Kind})"));
    private void ShowRoi()
    {
        if (displayed is not null) RoiImage.Source = ToBitmap(displayed.Crop(ParseRect(ClientText.Text)).Crop(ParseRect(RoiText.Text)));
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
        catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static NormalizedRect ParseRect(string text)
    {
        var values = text.Split(',').Select(Number).ToArray();
        if (values.Length != 4) throw new ArgumentException("Nhập X,Y,W,H, ví dụ 0.1,0.2,0.05,0.05.");
        var roi = new NormalizedRect(values[0], values[1], values[2], values[3]); roi.Validate(); return roi;
    }
    private static string RectText(NormalizedRect r) => FormattableString.Invariant($"{r.X},{r.Y},{r.Width},{r.Height}");
    internal static BitmapSource ToBitmap(GrayFrame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Pixels, frame.Width);
        bitmap.Freeze(); return bitmap;
    }
    internal static GrayFrame ReadScreenshot(string path, TimeSpan at)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Screenshot exceeds 32 MB.");
        using var stream = File.OpenRead(path);
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand).Frames[0];
        if (bitmap.PixelWidth > 7680 || bitmap.PixelHeight > 4320) throw new InvalidDataException("Screenshot too large.");
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth, 0);
        return new(0, at, converted.PixelWidth, converted.PixelHeight, pixels);
    }
}
