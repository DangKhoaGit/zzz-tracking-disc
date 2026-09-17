using System.Threading.Channels;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Application;

/// <summary>Owns the session. Only RunAsync mutates buff state; the UI reads immutable snapshots.</summary>
public sealed class TrackingService(
    IGameEventSource source, IBuffStateStore store, IClock clock, ITrackingLog log,
    IPresetRepository? presets = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private bool disposed;
    private volatile bool detectionEnabled = true;
    public bool DetectionEnabled => detectionEnabled;
    public void SetDetectionEnabled(bool enabled)
    {
        detectionEnabled = enabled;
        if (source is IDetectionControl control) control.SetDetectionEnabled(enabled);
    }
    public bool SubmitManual(GameEventType type, string subject) => source is IManualGameEventSource manual && manual.TrySend(type, subject);
    public bool SimulateCharacterChanged(string characterId) => source is IManualGameEventSource manual
        && manual.TrySend(GameEventType.CharacterChanged, characterId, false);

    public OverlaySnapshot Snapshot => store.Snapshot;

    public async Task StartAsync(CharacterPreset preset)
    {
        PresetRules.Validate(preset);
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is { IsCompleted: false }) return;
            StartCore(preset);
        }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    public async Task SwitchPresetAsync(CharacterPreset preset)
    {
        PresetRules.Validate(preset);
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await StopCoreAsync().ConfigureAwait(false);
            StartCore(preset);
        }
        finally { lifecycle.Release(); }
    }

    public async Task SelectCharacterAsync(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) throw new ArgumentException("Character ID is required.");
        if (presets is null) throw new InvalidOperationException("Preset repository is not configured.");
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var mapped = await presets.FindAsync(characterId, CancellationToken.None).ConfigureAwait(false);
            if (mapped is not null) PresetRules.Validate(mapped);
            await StopCoreAsync().ConfigureAwait(false);
            StartCore(mapped ?? new CharacterPreset(characterId, 1, []), mapped is null);
        }
        finally { lifecycle.Release(); }
    }

    private void StartCore(CharacterPreset preset, bool notConfigured = false)
    {
        cancellation?.Dispose();
        cancellation = new();
        var context = new TrackingContext(Guid.NewGuid(), preset);
        var token = cancellation.Token;
        store.Publish(new(context.SessionId, notConfigured ? TrackingStatus.NotConfigured : TrackingStatus.Manual, [], 0, CharacterId: preset.CharacterId));
        worker = Task.Run(() => RunAsync(context, token, notConfigured), CancellationToken.None);
    }

    private async Task StopCoreAsync()
    {
        if (cancellation is null) return;
        await cancellation.CancelAsync().ConfigureAwait(false);
        if (worker is not null) await worker.ConfigureAwait(false);
        cancellation.Dispose();
        cancellation = null;
        worker = null;
        store.Publish(store.Snapshot with { Status = TrackingStatus.Stopped, Buffs = [] });
    }

    private async Task RunAsync(TrackingContext context, CancellationToken token, bool notConfigured = false)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var runToken = runCancellation.Token;
        // Semantic events are lossless with backpressure. DropOldest is only for future raw frames.
        var events = CreateQueue();
        var engine = new BuffRuleEngine(context, clock);
        var sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        var latestCharacterAt = TimeSpan.MinValue;
        Task producer = Task.CompletedTask;
        try
        {
            Publish();
            log.Write("Information", "Tracking started (simulation/manual).", context.SessionId);
            producer = ProduceAsync(context, events.Writer, sourceCancellation.Token);
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            while (await ticker.WaitForNextTickAsync(runToken).ConfigureAwait(false))
            {
                // A finite batch prevents a fast producer from starving expiry/projection.
                for (var i = 0; i < 128 && events.Reader.TryRead(out var e); i++)
                {
                    if (!detectionEnabled && !e.IsManual)
                    {
                        log.Write("Debug", "Detection paused; automatic event ignored.", context.SessionId, e.EventId);
                        continue;
                    }
                    if (e.Type == GameEventType.CharacterChanged && presets is not null)
                    {
                        if (e.SessionId != context.SessionId || e.PresetVersion != context.Preset.Version
                            || !double.IsFinite(e.Confidence) || e.Confidence < 0.9 || e.Confidence > 1
                            || string.IsNullOrWhiteSpace(e.SubjectId) || e.CapturedAt < TimeSpan.Zero
                            || e.CapturedAt > clock.Elapsed || clock.Elapsed - e.CapturedAt >= RuleEngineOptions.MaximumEventAge
                            || e.CapturedAt <= latestCharacterAt) continue;
                        latestCharacterAt = e.CapturedAt;
                        if (e.SubjectId == context.Preset.CharacterId && !notConfigured) continue;
                        var mapped = await presets.FindAsync(e.SubjectId, runToken).ConfigureAwait(false);
                        if (mapped is not null) PresetRules.Validate(mapped);
                        await sourceCancellation.CancelAsync().ConfigureAwait(false);
                        await producer.ConfigureAwait(false);
                        sourceCancellation.Dispose();
                        sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                        // A new source inbox/context drops all buffered events from the previous character.
                        context = new(Guid.NewGuid(), mapped ?? new CharacterPreset(e.SubjectId, 1, []));
                        notConfigured = mapped is null;
                        engine = new(context, clock);
                        events = CreateQueue();
                        producer = ProduceAsync(context, events.Writer, sourceCancellation.Token);
                        log.Write("Information", $"CharacterChanged={e.SubjectId}; configured={!notConfigured}", context.SessionId, e.EventId);
                        break;
                    }
                    if (notConfigured) continue;
                    var result = engine.Process(e);
                    log.Write("Debug", $"Decision={result.Decision}; changed={result.ChangedBuffs}; processedAt={result.ProcessedAt}; correlation={e.CorrelationKey}", context.SessionId, e.EventId);
                }
                if (events.Reader.Completion.IsCompleted) await events.Reader.Completion.ConfigureAwait(false);
                Publish();
            }
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            store.Publish(store.Snapshot with { Status = TrackingStatus.Faulted, Error = ex.Message, Buffs = [] });
            log.Write("Error", "Tracking worker failed.", context.SessionId, exception: ex);
        }
        finally
        {
            await runCancellation.CancelAsync().ConfigureAwait(false);
            try { await producer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested) { }
            sourceCancellation.Dispose();
            log.Write("Information", "Tracking worker stopped.", context.SessionId);
        }

        async Task ProduceAsync(TrackingContext sourceContext, ChannelWriter<GameEvent> writer, CancellationToken sourceToken)
        {
            try
            {
                await foreach (var e in source.ReadEventsAsync(sourceContext, sourceToken).ConfigureAwait(false))
                    await writer.WriteAsync(e, sourceToken).ConfigureAwait(false);
                writer.TryComplete();
            }
            catch (OperationCanceledException) when (sourceToken.IsCancellationRequested) { writer.TryComplete(); }
            catch (Exception ex) { writer.TryComplete(ex); }
        }

        void Publish() => store.Publish(engine.Snapshot() with
        { Status = notConfigured ? TrackingStatus.NotConfigured : TrackingStatus.Manual });
        static Channel<GameEvent> CreateQueue() => Channel.CreateBounded<GameEvent>(new BoundedChannelOptions(128)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }
}
