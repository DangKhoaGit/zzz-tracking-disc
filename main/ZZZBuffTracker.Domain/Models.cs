using System.Collections.Immutable;

namespace ZZZBuffTracker.Domain;

public enum BuffStatus { Inactive, Pending, Active, Unknown, Expired }
public enum GameEventType
{
    ManualTrigger, Reset, BuffIconAppeared, BuffIconDisappeared, CharacterChanged,
    ManualRefresh, ManualStack, ManualDeactivate, ManualExpire, BuffCandidate, SignalLost
}
public enum TrackingStatus { Stopped, Manual, Faulted, NotConfigured }

// CapturedAt is monotonic elapsed time from the injected clock, never wall-clock time.
public sealed record GameEvent(Guid EventId, GameEventType Type, string SubjectId,
    TimeSpan CapturedAt, double Confidence, string Source, string CorrelationKey,
    Guid SessionId, int PresetVersion, bool IsManual = false, DetectionEvidence? Evidence = null);

public sealed record DetectionEvidence(string PackId, int PackVersion, string TemplateId, int TemplateVersion,
    long FrameSequence, int FrameWidth, int FrameHeight, double RoiX, double RoiY, double RoiWidth,
    double RoiHeight, double Similarity, TimeSpan ProcessedAt);

public enum RetriggerMode { Refresh, Stack, Ignore }
public enum SignalLossPolicy { ExpireByTimer, WaitForDisappear, KeepUnknown }
public enum BuffAction { Activate, Refresh, Stack, Deactivate, Expire, Reset, SetPending, SignalLost }

public sealed record BuffDefinition(string Id, string Name, TimeSpan Duration, int MaxStacks = 1,
    RetriggerMode Retrigger = RetriggerMode.Refresh,
    SignalLossPolicy SignalLoss = SignalLossPolicy.ExpireByTimer, bool StackRefreshesDuration = true,
    string? DriveDiscSetId = null);
public sealed record BuffRule(string Id, GameEventType Trigger, string SubjectId, string BuffId,
    BuffAction Action, double MinimumConfidence = 0.8, TimeSpan Cooldown = default);
public sealed record CharacterPreset(string CharacterId, int Version, ImmutableArray<BuffDefinition> Buffs,
    ImmutableArray<BuffRule> Rules = default);
public sealed record BuffState(string BuffId, BuffStatus Status, TimeSpan? ExpiresAt, int Stacks);
public sealed record BuffSnapshot(string Id, string Name, BuffStatus Status, double RemainingSeconds,
    double Progress, int Stacks);
public sealed record OverlaySnapshot(Guid SessionId, TrackingStatus Status,
    ImmutableArray<BuffSnapshot> Buffs, long ProcessedEvents, string? Error = null,
    long RejectedEvents = 0, string? LastDecision = null, string? CharacterId = null)
{
    public static OverlaySnapshot Empty { get; } = new(Guid.Empty, TrackingStatus.Stopped, [], 0);
}
