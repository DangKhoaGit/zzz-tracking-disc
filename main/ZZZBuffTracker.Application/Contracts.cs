using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Application;

public interface IClock { TimeSpan Elapsed { get; } }
public sealed record TrackingContext(Guid SessionId, CharacterPreset Preset);

public interface IGameEventSource
{
    IAsyncEnumerable<GameEvent> ReadEventsAsync(TrackingContext context, CancellationToken cancellationToken);
}

public interface IManualGameEventSource : IGameEventSource
{
    bool TrySend(GameEventType type, string subject, bool isManual = true);
}

public interface IDetectionControl
{
    void SetDetectionEnabled(bool enabled);
}

public interface IBuffStateStore
{
    OverlaySnapshot Snapshot { get; }
    void Publish(OverlaySnapshot snapshot);
}

public interface IPresetRepository
{
    Task<CharacterPreset?> FindAsync(string characterId, CancellationToken cancellationToken);
    Task SaveAsync(CharacterPreset preset, CancellationToken cancellationToken);
}

public interface ITrackingLog
{
    void Write(string level, string message, Guid sessionId, Guid? eventId = null, Exception? exception = null);
}
