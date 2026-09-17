using System.Collections.Immutable;

namespace ZZZBuffTracker.Domain;

public static class PresetRules
{
    // Compatibility defaults for Phase 01/02 presets. Explicit [] means no buff rules.
    public static ImmutableArray<BuffRule> Resolve(CharacterPreset preset) => !preset.Rules.IsDefault
        ? preset.Rules : preset.Buffs.SelectMany(d => new[]
        {
            Rule(d, GameEventType.ManualTrigger, BuffAction.Activate),
            Rule(d, GameEventType.ManualRefresh, BuffAction.Refresh),
            Rule(d, GameEventType.ManualStack, BuffAction.Stack),
            Rule(d, GameEventType.ManualDeactivate, BuffAction.Deactivate),
            Rule(d, GameEventType.ManualExpire, BuffAction.Expire),
            Rule(d, GameEventType.BuffCandidate, BuffAction.SetPending),
            Rule(d, GameEventType.SignalLost, BuffAction.SignalLost),
            Rule(d, GameEventType.BuffIconAppeared, BuffAction.Activate, TimeSpan.FromMilliseconds(150)),
            Rule(d, GameEventType.BuffIconDisappeared, BuffAction.Deactivate, TimeSpan.FromMilliseconds(150))
        }).ToImmutableArray();

    private static BuffRule Rule(BuffDefinition d, GameEventType trigger, BuffAction action, TimeSpan cooldown = default) =>
        new(d.Id + ":" + trigger, trigger, d.Id, d.Id, action, Cooldown: cooldown);

    public static void Validate(CharacterPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(preset.CharacterId) || preset.Version < 1 || preset.Buffs.IsDefault)
            throw new ArgumentException("Preset must have a character, positive version and initialized buffs.", nameof(preset));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var buff in preset.Buffs)
            if (buff is null || string.IsNullOrWhiteSpace(buff.Id) || !ids.Add(buff.Id) || string.IsNullOrWhiteSpace(buff.Name)
                || buff.Duration <= TimeSpan.Zero || buff.Duration > TimeSpan.FromDays(1) || buff.MaxStacks < 1
                || !Enum.IsDefined(buff.Retrigger) || !Enum.IsDefined(buff.SignalLoss))
                throw new ArgumentException("Invalid or duplicate buff definition.", nameof(preset));
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<(GameEventType, string, string)>();
        foreach (var rule in Resolve(preset))
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !ruleIds.Add(rule.Id)
                || string.IsNullOrWhiteSpace(rule.SubjectId) || !ids.Contains(rule.BuffId)
                || !Enum.IsDefined(rule.Trigger) || rule.Trigger == GameEventType.Reset || !Enum.IsDefined(rule.Action)
                || !double.IsFinite(rule.MinimumConfidence) || rule.MinimumConfidence < 0 || rule.MinimumConfidence > 1
                || rule.Cooldown < TimeSpan.Zero || rule.Cooldown > TimeSpan.FromSeconds(30)
                || !targets.Add((rule.Trigger, rule.SubjectId, rule.BuffId)))
                throw new ArgumentException("Invalid/ambiguous buff rule (cooldown must be 0–30 seconds).", nameof(preset));
    }
}
