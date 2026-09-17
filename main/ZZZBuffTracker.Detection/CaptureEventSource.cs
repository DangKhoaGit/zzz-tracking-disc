using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Detection;

public enum CaptureStatus { Manual, Waiting, Capturing, Paused, CaptureUnavailable, ReplayComplete }
public sealed record CaptureDiagnostics(CaptureStatus Status, string Message, long Frames = 0,
    long DroppedFrames = 0, double LatencyMilliseconds = 0, string Decisions = "");

/// <summary>Manual inbox survives capture failure. Each context owns and cancels its capture resources.</summary>
public sealed class CaptureEventSource(IClock clock, ITrackingLog log) : IManualGameEventSource, IDetectionControl
{
    private sealed record Configuration(IFrameSource Source, TemplatePack? Pack);
    private readonly object gate = new();
    private Channel<GameEvent>? inbox;
    private TrackingContext? active;
    private Configuration? configuration;
    private long revision;
    private bool enabled = true;
    private CaptureDiagnostics diagnostics = new(CaptureStatus.Manual, "Manual fallback; chưa chọn capture.");
    private GrayFrame? latestFrame;
    public CaptureDiagnostics Diagnostics => Volatile.Read(ref diagnostics);
    public GrayFrame? LatestFrame => Volatile.Read(ref latestFrame);
    public TemplatePack? ConfiguredPack { get { lock (gate) return configuration?.Pack; } }
    public IFrameSource? ConfiguredSource { get { lock (gate) return configuration?.Source; } }
    public void Configure(IFrameSource? source, TemplatePack? pack)
    {
        pack?.Validate();
        lock (gate) { configuration = source is null ? null : new(source, pack!); revision++; }
        Volatile.Write(ref latestFrame, null);
        SetStatus(source is null ? CaptureStatus.Manual : CaptureStatus.Waiting, "Cấu hình đã chọn; Bắt đầu tracking để chạy.");
    }
    public void SetDetectionEnabled(bool value) { lock (gate) { if (enabled == value) return; enabled = value; revision++; } }
    public bool TrySend(GameEventType type, string subject, bool isManual = true)
    {
        lock (gate)
        {
            if (active is null || inbox is null) return false;
            var id = Guid.NewGuid();
            return inbox.Writer.TryWrite(new(id, type, subject, clock.Elapsed, 1, "ManualSimulation", id.ToString("N"),
                active.SessionId, active.Preset.Version, isManual));
        }
    }

    public async IAsyncEnumerable<GameEvent> ReadEventsAsync(TrackingContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<GameEvent>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
        lock (gate)
        {
            if (inbox is not null) throw new InvalidOperationException("Source already has a reader.");
            inbox = channel; active = context;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var capture = SuperviseAsync(context, channel.Writer, stop.Token);
        try { await foreach (var e in channel.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false)) yield return e; }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await capture.ConfigureAwait(false);
            lock (gate) { inbox = null; active = null; channel.Writer.TryComplete(); }
            SetStatus(CaptureStatus.Waiting, "Tracking đã dừng; capture resources đã đóng.");
        }
    }

    private async Task SuperviseAsync(TrackingContext context, ChannelWriter<GameEvent> events, CancellationToken token)
    {
        long applied = -1;
        CancellationTokenSource? runStop = null;
        Task run = Task.CompletedTask;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Configuration? selected; bool isEnabled; long current;
                lock (gate) { selected = configuration; isEnabled = enabled; current = revision; }
                if (current != applied)
                {
                    if (runStop is not null) { await runStop.CancelAsync().ConfigureAwait(false); await run.ConfigureAwait(false); runStop.Dispose(); }
                    applied = current;
                    runStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                    run = selected is not null && isEnabled
                        ? RunCaptureAsync(selected, context, events, runStop.Token) : Task.CompletedTask;
                    if (!isEnabled || selected is null) SetStatus(isEnabled ? CaptureStatus.Manual : CaptureStatus.Paused,
                        isEnabled ? "Manual fallback." : "Capture đã tạm dừng; manual và timer vẫn hoạt động.");
                }
                await Task.Delay(75, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (runStop is not null) { await runStop.CancelAsync().ConfigureAwait(false); await run.ConfigureAwait(false); runStop.Dispose(); }
        }
    }

    private async Task RunCaptureAsync(Configuration config, TrackingContext context, ChannelWriter<GameEvent> events, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        long dropped = 0, count = 0;
        var frames = Channel.CreateBounded<GrayFrame>(new BoundedChannelOptions(2)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest },
            _ => Interlocked.Increment(ref dropped));
        var detector = config.Pack is null ? null : new TemplateDetector(config.Pack);
        SetStatus(CaptureStatus.Waiting, "Đang mở nguồn frame…");
        var producer = ProduceAsync();
        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                frame.Validate();
                Volatile.Write(ref latestFrame, frame);
                var result = detector?.Process(frame, context, clock.Elapsed) ?? new DetectionResult([], []);
                foreach (var e in result.Events)
                {
                    await events.WriteAsync(e with { Source = $"TemplateMatcher/{config.Source.GetType().Name}" }, stop.Token).ConfigureAwait(false);
                    log.Write("Information", $"Detection evidence={System.Text.Json.JsonSerializer.Serialize(e.Evidence)}; type={e.Type}", context.SessionId, e.EventId);
                }
                var decisions = string.Join("; ", result.Decisions.Select(d => $"{d.TemplateId}: {d.Score:F3} {d.Decision}"));
                if (count % 10 == 0) log.Write("Debug", $"Detection decisions: {decisions}", context.SessionId);
                var latency = Math.Max(0, (clock.Elapsed - frame.CapturedAt).TotalMilliseconds);
                Volatile.Write(ref diagnostics, new(CaptureStatus.Capturing, "Đang nhận diện", ++count, Interlocked.Read(ref dropped), latency, decisions));
            }
            SetStatus(CaptureStatus.ReplayComplete, "Nguồn frame kết thúc. Manual vẫn hoạt động.");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetStatus(CaptureStatus.CaptureUnavailable, ex.Message + " Dùng manual hoặc chọn lại nguồn để thử lại.");
            log.Write("Warning", "CaptureUnavailable", context.SessionId, exception: ex);
            // Capture loss is not evidence of icon disappearance. SignalLost lets the configured rule policy decide.
            foreach (var buff in context.Preset.Buffs)
            {
                var id = Guid.NewGuid();
                try
                {
                    await events.WriteAsync(new(id, GameEventType.SignalLost, buff.Id, clock.Elapsed, 1,
                        "CaptureHealth", id.ToString("N"), context.SessionId, context.Preset.Version), stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            }
        }
        finally { await stop.CancelAsync().ConfigureAwait(false); await producer.ConfigureAwait(false); }

        async Task ProduceAsync()
        {
            try
            {
                await foreach (var frame in config.Source.ReadFramesAsync(stop.Token).ConfigureAwait(false))
                    await frames.Writer.WriteAsync(frame, stop.Token).ConfigureAwait(false);
                frames.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { frames.Writer.TryComplete(); }
            catch (Exception ex) { frames.Writer.TryComplete(ex); }
        }
    }
    private void SetStatus(CaptureStatus status, string message) => Volatile.Write(ref diagnostics, Diagnostics with { Status = status, Message = message });
}
