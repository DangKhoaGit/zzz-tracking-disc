using System.Collections.Immutable;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Application;

public enum EventDecision
{
    Applied, WrongSession, InvalidEvent, TooOld, OutOfOrder, Duplicate,
    NoRule, LowConfidence, Cooldown, NoChange, CapacityExceeded
}
public sealed record RuleEngineOptions(int DedupCapacity = 4096)
{
    public static readonly TimeSpan MaximumEventAge = TimeSpan.FromSeconds(30);
}
public sealed record EventResult(EventDecision Decision, int ChangedBuffs, TimeSpan ProcessedAt)
{
    public bool Accepted => Decision == EventDecision.Applied;
}

/// <summary>
/// Session-scoped, single-writer engine. Call only from the owning worker.
/// Keep raw event state separate from time projection so delayed events retain correct semantics.
/// </summary>
public sealed class BuffRuleEngine
{
    private readonly TrackingContext context;
    private readonly IClock clock;
    private readonly int capacity;
    private readonly Dictionary<string, BuffDefinition> definitions;
    private readonly Dictionary<(GameEventType, string), ImmutableArray<BuffRule>> rules;
    private readonly Dictionary<string, BuffState> states;
    private readonly Dictionary<string, TimeSpan> lastEventAt = [];
    private readonly HashSet<Guid> seen = [];
    private readonly Dictionary<Correlation, Remembered> correlations = [];
    private readonly Queue<Receipt> receipts = new();
    private TimeSpan resetAt = TimeSpan.MinValue;
    private TimeSpan lastNow;
    private long accepted;
    private long rejected;
    private EventDecision? lastDecision;
    private sealed record Correlation(string RuleId, string Source, string Key);
    private sealed record Remembered(Guid EventId, TimeSpan CapturedAt);
    private sealed record Receipt(Guid EventId, TimeSpan At, ImmutableArray<Correlation> Keys);

    public BuffRuleEngine(TrackingContext context, IClock clock, RuleEngineOptions? options = null)
    {
        PresetRules.Validate(context.Preset);
        if (context.SessionId == Guid.Empty) throw new ArgumentException("Session must not be empty.", nameof(context));
        capacity = (options ?? new()).DedupCapacity;
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(options));
        this.context = context;
        this.clock = clock;
        definitions = context.Preset.Buffs.ToDictionary(x => x.Id);
        states = definitions.Keys.ToDictionary(x => x, x => new BuffState(x, BuffStatus.Inactive, null, 0));
        rules = PresetRules.Resolve(context.Preset).GroupBy(x => (x.Trigger, x.SubjectId))
            .ToDictionary(x => x.Key, x => x.ToImmutableArray());
    }

    public int RememberedEventCount => seen.Count;
    public int RememberedCorrelationCount => correlations.Count;

    public EventResult Process(GameEvent e)
    {
        var now = ReadNow();
        Prune(now);
        if (e.SessionId != context.SessionId || e.PresetVersion != context.Preset.Version) return Finish(EventDecision.WrongSession);
        if (e.EventId == Guid.Empty || !Enum.IsDefined(e.Type) || string.IsNullOrWhiteSpace(e.SubjectId)
            || string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.CorrelationKey)
            || !double.IsFinite(e.Confidence) || e.Confidence < 0 || e.Confidence > 1
            || e.CapturedAt < TimeSpan.Zero || e.CapturedAt > now
            || e.CapturedAt > TimeSpan.MaxValue - TimeSpan.FromDays(1)) return Finish(EventDecision.InvalidEvent);
        if (now - e.CapturedAt >= RuleEngineOptions.MaximumEventAge) return Finish(EventDecision.TooOld);
        if (seen.Contains(e.EventId)) return Finish(EventDecision.Duplicate);
        if (e.CapturedAt <= resetAt) return Finish(EventDecision.OutOfOrder);
        if (e.Type == GameEventType.Reset)
        {
            if (e.SubjectId != "*") return Finish(EventDecision.NoRule);
            if (e.Confidence < 0.8) return Finish(EventDecision.LowConfidence);
            if (lastEventAt.Values.Any(at => at > e.CapturedAt)) return Finish(EventDecision.OutOfOrder);
            foreach (var id in states.Keys) states[id] = new(id, BuffStatus.Inactive, null, 0);
            resetAt = e.CapturedAt;
            // Retain dedup history so queued copies cannot undo the reset.
            if (seen.Count < capacity) Remember([]);
            return Finish(EventDecision.Applied, states.Count);
        }
        if (seen.Count >= capacity) return Finish(EventDecision.CapacityExceeded);
        if (!rules.TryGetValue((e.Type, e.SubjectId), out var matching)) return Finish(EventDecision.NoRule);

        var changes = new List<(BuffRule Rule, BuffState State, Correlation Key)>();
        var reason = EventDecision.NoChange;
        foreach (var rule in matching)
        {
            if (e.Confidence < rule.MinimumConfidence) { reason = EventDecision.LowConfidence; continue; }
            if (lastEventAt.TryGetValue(rule.BuffId, out var last) && e.CapturedAt < last)
            { reason = EventDecision.OutOfOrder; continue; }
            var key = new Correlation(rule.Id, e.Source, e.CorrelationKey);
            if (rule.Cooldown > TimeSpan.Zero && correlations.TryGetValue(key, out var previous)
                && e.CapturedAt - previous.CapturedAt < rule.Cooldown)
            { reason = EventDecision.Cooldown; continue; }
            var definition = definitions[rule.BuffId];
            var before = BuffTransitions.Advance(definition, states[rule.BuffId], e.CapturedAt);
            var after = BuffTransitions.Apply(definition, before, rule.Action, e.CapturedAt);
            // Even an unchanged confirmed observation advances ordering, preventing stale loss
            // or disappearance events from overriding a newer signal.
            changes.Add((rule, after, key));
        }
        if (changes.Count == 0) return Finish(reason);
        var keys = changes.Where(x => x.Rule.Cooldown > TimeSpan.Zero).Select(x => x.Key).ToImmutableArray();
        if (correlations.Count + keys.Count(key => !correlations.ContainsKey(key)) > capacity)
            return Finish(EventDecision.CapacityExceeded);
        var changedCount = 0;
        foreach (var change in changes)
        {
            if (states[change.Rule.BuffId] != change.State) changedCount++;
            states[change.Rule.BuffId] = change.State;
            lastEventAt[change.Rule.BuffId] = e.CapturedAt;
        }
        Remember(keys);
        return Finish(EventDecision.Applied, changedCount);

        void Remember(ImmutableArray<Correlation> keys)
        {
            seen.Add(e.EventId);
            foreach (var key in keys) correlations[key] = new(e.EventId, e.CapturedAt);
            receipts.Enqueue(new(e.EventId, now, keys));
        }
        EventResult Finish(EventDecision decision, int count = 0)
        {
            lastDecision = decision;
            if (decision == EventDecision.Applied) accepted++; else rejected++;
            return new(decision, count, now);
        }
    }

    public OverlaySnapshot Snapshot()
    {
        var now = ReadNow();
        Prune(now);
        var buffs = ImmutableArray.CreateBuilder<BuffSnapshot>(states.Count);
        foreach (var definition in context.Preset.Buffs)
        {
            var state = BuffTransitions.Advance(definition, states[definition.Id], now);
            var seconds = state.Status == BuffStatus.Active && state.ExpiresAt is { } end
                ? Math.Max(0, (end - now).TotalSeconds) : 0;
            buffs.Add(new(definition.Id, definition.Name, state.Status, seconds,
                Math.Clamp(seconds / definition.Duration.TotalSeconds, 0, 1), state.Stacks));
        }
        return new(context.SessionId, TrackingStatus.Manual, buffs.MoveToImmutable(), accepted,
            RejectedEvents: rejected, LastDecision: lastDecision?.ToString(), CharacterId: context.Preset.CharacterId);
    }

    private TimeSpan ReadNow()
    {
        var now = clock.Elapsed;
        if (now < lastNow) throw new InvalidOperationException("IClock must be monotonic.");
        lastNow = now;
        return now;
    }
    private void Prune(TimeSpan now)
    {
        while (receipts.TryPeek(out var receipt) && now - receipt.At >= RuleEngineOptions.MaximumEventAge)
        {
            receipts.Dequeue();
            seen.Remove(receipt.EventId);
            foreach (var key in receipt.Keys)
                if (correlations.TryGetValue(key, out var remembered) && remembered.EventId == receipt.EventId)
                    correlations.Remove(key);
        }
    }
}
