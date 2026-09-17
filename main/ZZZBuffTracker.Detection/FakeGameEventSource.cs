using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Detection;

/// <summary>Development source: a new inbox is created for each tracking session.</summary>
public sealed class FakeGameEventSource(IClock clock) : IManualGameEventSource
{
    private readonly object gate = new();
    private Channel<GameEvent>? inbox;
    private TrackingContext? active;

    // Explicit rejection when stopped/full; manual commands are never silently dropped.
    public bool TryTrigger(string buffId) => TrySend(GameEventType.ManualTrigger, buffId);
    public bool TrySimulate(GameEventType type, string buffId) => TrySend(type, buffId);
    public bool TryReset() => TrySend(GameEventType.Reset, "*");

    public bool TrySend(GameEventType type, string subject, bool isManual = true)
    {
        lock (gate)
        {
            if (active is null || inbox is null) return false;
            var id = Guid.NewGuid();
            return inbox.Writer.TryWrite(new(id, type, subject, clock.Elapsed, 1, "ManualSimulation",
                id.ToString("N"), active.SessionId, active.Preset.Version, isManual));
        }
    }

    public async IAsyncEnumerable<GameEvent> ReadEventsAsync(TrackingContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<GameEvent> channel;
        lock (gate)
        {
            if (inbox is not null) throw new InvalidOperationException("Source already has a reader.");
            channel = Channel.CreateBounded<GameEvent>(new BoundedChannelOptions(64)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            inbox = channel;
            active = context;
        }
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return e;
        }
        finally
        {
            lock (gate) { channel.Writer.TryComplete(); inbox = null; active = null; }
        }
    }
}
