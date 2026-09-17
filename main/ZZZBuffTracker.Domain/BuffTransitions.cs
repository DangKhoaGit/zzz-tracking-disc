namespace ZZZBuffTracker.Domain;

/// <summary>Pure transitions. The caller supplies monotonic event time; no timers, UI or shared state.</summary>
public static class BuffTransitions
{
    public static BuffState Apply(BuffDefinition definition, BuffState state, BuffAction action, TimeSpan at)
    {
        var hasStacks = state.Stacks > 0 && state.Status is BuffStatus.Active or BuffStatus.Unknown;
        if (action == BuffAction.Activate && hasStacks)
        {
            if (definition.Retrigger == RetriggerMode.Ignore) return state;
            action = definition.Retrigger == RetriggerMode.Stack ? BuffAction.Stack : BuffAction.Refresh;
        }
        return action switch
        {
            BuffAction.Activate => new(state.BuffId, BuffStatus.Active, at + definition.Duration, 1),
            BuffAction.Refresh when hasStacks => state with { Status = BuffStatus.Active, ExpiresAt = at + definition.Duration },
            BuffAction.Stack => new(state.BuffId, BuffStatus.Active,
                !hasStacks || definition.StackRefreshesDuration ? at + definition.Duration : state.ExpiresAt,
                hasStacks ? (int)Math.Min((long)state.Stacks + 1, definition.MaxStacks) : 1),
            BuffAction.Deactivate or BuffAction.Reset => new(state.BuffId, BuffStatus.Inactive, null, 0),
            BuffAction.Expire => new(state.BuffId, BuffStatus.Expired, null, 0),
            BuffAction.SetPending when !hasStacks => new(state.BuffId, BuffStatus.Pending, null, 0),
            BuffAction.SignalLost when definition.SignalLoss != SignalLossPolicy.ExpireByTimer =>
                state with { Status = BuffStatus.Unknown },
            _ => state
        };
    }

    public static BuffState Advance(BuffDefinition definition, BuffState state, TimeSpan now)
    {
        if (state.ExpiresAt is not { } end || end > now) return state;
        if (state.Status == BuffStatus.Unknown && definition.SignalLoss != SignalLossPolicy.ExpireByTimer) return state;
        if (state.Status != BuffStatus.Active) return state;
        return definition.SignalLoss == SignalLossPolicy.WaitForDisappear
            ? state with { Status = BuffStatus.Unknown }
            : state with { Status = BuffStatus.Expired, Stacks = 0 };
    }
}
