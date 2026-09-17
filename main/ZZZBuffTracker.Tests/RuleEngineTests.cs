using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Tests;

[TestClass]
public sealed class RuleEngineTests
{
    private static readonly Guid Session = Guid.Parse("49C9358D-16C3-48E1-94BD-D5A746474BB1");
    private static BuffDefinition Definition => new("buff", "Test", TimeSpan.FromSeconds(6), MaxStacks: 3);
    private static CharacterPreset Preset(BuffDefinition? definition = null) => new("character", 1, [definition ?? Definition]);
    private static BuffRuleEngine Engine(Clock clock, CharacterPreset? preset = null, int capacity = 4096) =>
        new(new(Session, preset ?? Preset()), clock, new(capacity));
    private static GameEvent Event(GameEventType type, double at = 0, string subject = "buff", string? correlation = null,
        double confidence = 1) => new(Guid.NewGuid(), type, subject, TimeSpan.FromSeconds(at), confidence,
            "test-source", correlation ?? Guid.NewGuid().ToString(), Session, 1);
    private static BuffSnapshot Buff(BuffRuleEngine engine) => engine.Snapshot().Buffs[0];
    private sealed class Clock : IClock
    {
        public TimeSpan Elapsed { get; set; }
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UtcNow;
        public void At(double seconds) => Elapsed = TimeSpan.FromSeconds(seconds);
    }

    [TestMethod]
    public void ActivateRefreshExpireResetAndDeactivate()
    {
        var clock = new Clock(); var engine = Engine(clock);
        Assert.IsTrue(engine.Process(Event(GameEventType.ManualTrigger)).Accepted);
        Assert.AreEqual(6d, Buff(engine).RemainingSeconds);
        clock.At(2);
        engine.Process(Event(GameEventType.ManualRefresh, 2));
        Assert.AreEqual(1, Buff(engine).Stacks);
        clock.At(8);
        Assert.AreEqual(BuffStatus.Expired, Buff(engine).Status);
        Assert.AreEqual(0d, Buff(engine).RemainingSeconds);
        engine.Process(Event(GameEventType.Reset, 8, "*"));
        Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        clock.At(9); engine.Process(Event(GameEventType.ManualTrigger, 9));
        engine.Process(Event(GameEventType.ManualDeactivate, 9));
        Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        clock.At(10); engine.Process(Event(GameEventType.ManualTrigger, 10));
        engine.Process(Event(GameEventType.ManualExpire, 10));
        Assert.AreEqual(BuffStatus.Expired, Buff(engine).Status);
    }

    [TestMethod]
    public void StackCapsAndRefreshDoesNotIncreaseStack()
    {
        var clock = new Clock(); var engine = Engine(clock);
        for (var i = 0; i < 7; i++) { clock.At(i); engine.Process(Event(GameEventType.ManualStack, i)); }
        Assert.AreEqual(3, Buff(engine).Stacks);
        clock.At(8); engine.Process(Event(GameEventType.ManualRefresh, 8));
        Assert.AreEqual(3, Buff(engine).Stacks);
        Assert.AreEqual(6d, Buff(engine).RemainingSeconds);
    }

    [TestMethod]
    public void RetriggerModesAndNonRefreshingStacks()
    {
        foreach (var mode in Enum.GetValues<RetriggerMode>())
        {
            var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { Retrigger = mode }));
            engine.Process(Event(GameEventType.ManualTrigger));
            clock.At(2); engine.Process(Event(GameEventType.ManualTrigger, 2));
            Assert.AreEqual(mode == RetriggerMode.Stack ? 2 : 1, Buff(engine).Stacks);
            Assert.AreEqual(mode == RetriggerMode.Ignore ? 4d : 6d, Buff(engine).RemainingSeconds);
        }
        var fixedClock = new Clock(); var fixedEngine = Engine(fixedClock, Preset(Definition with { StackRefreshesDuration = false }));
        fixedEngine.Process(Event(GameEventType.ManualStack));
        fixedClock.At(2); fixedEngine.Process(Event(GameEventType.ManualStack, 2));
        Assert.AreEqual(2, Buff(fixedEngine).Stacks);
        Assert.AreEqual(4d, Buff(fixedEngine).RemainingSeconds);
    }

    [TestMethod]
    public void DuplicateIdAndCorrelationCooldownCannotAddStacks()
    {
        var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { Retrigger = RetriggerMode.Stack }));
        var first = Event(GameEventType.BuffIconAppeared, correlation: "icon-edge");
        engine.Process(first);
        Assert.AreEqual(EventDecision.Duplicate, engine.Process(first).Decision);
        clock.At(0.1);
        Assert.AreEqual(EventDecision.Cooldown, engine.Process(Event(GameEventType.BuffIconAppeared, 0.1, correlation: "icon-edge")).Decision);
        Assert.AreEqual(1, Buff(engine).Stacks);
        clock.At(0.15);
        Assert.IsTrue(engine.Process(Event(GameEventType.BuffIconAppeared, 0.15, correlation: "icon-edge")).Accepted);
        Assert.AreEqual(2, Buff(engine).Stacks);
    }

    [TestMethod]
    public void PerRuleConfidenceAndSubjectMapping()
    {
        var preset = new CharacterPreset("character", 1, [Definition, Definition with { Id = "second" }],
            [new("strict", GameEventType.BuffIconAppeared, "icon", "buff", BuffAction.Activate, 0.95),
             new("lenient", GameEventType.BuffIconAppeared, "icon", "second", BuffAction.Activate, 0.6)]);
        var engine = Engine(new(), preset);
        Assert.AreEqual(1, engine.Process(Event(GameEventType.BuffIconAppeared, subject: "icon", confidence: 0.8)).ChangedBuffs);
        Assert.AreEqual(BuffStatus.Inactive, engine.Snapshot().Buffs[0].Status);
        Assert.AreEqual(BuffStatus.Active, engine.Snapshot().Buffs[1].Status);
        Assert.AreEqual(EventDecision.NoRule, engine.Process(Event(GameEventType.ManualTrigger)).Decision);
    }

    [TestMethod]
    public void RejectsLowConfidenceInvalidTimesAndWrongContext()
    {
        var clock = new Clock(); clock.At(40); var engine = Engine(clock);
        var valid = Event(GameEventType.ManualTrigger, 40);
        Assert.AreEqual(EventDecision.LowConfidence, engine.Process(valid with { Confidence = 0.1 }).Decision);
        Assert.AreEqual(EventDecision.InvalidEvent, engine.Process(valid with { Confidence = double.NaN }).Decision);
        Assert.AreEqual(EventDecision.InvalidEvent, engine.Process(valid with { CapturedAt = TimeSpan.FromSeconds(41) }).Decision);
        Assert.AreEqual(EventDecision.InvalidEvent, engine.Process(valid with { CapturedAt = TimeSpan.FromSeconds(-1) }).Decision);
        Assert.AreEqual(EventDecision.TooOld, engine.Process(valid with { CapturedAt = TimeSpan.FromSeconds(10) }).Decision);
        Assert.AreEqual(EventDecision.WrongSession, engine.Process(valid with { SessionId = Guid.NewGuid() }).Decision);
        Assert.AreEqual(EventDecision.WrongSession, engine.Process(valid with { PresetVersion = 2 }).Decision);
        Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        Assert.IsTrue(engine.Process(valid).Accepted);
    }

    [TestMethod]
    public void LateRefreshUsesCaptureTimeEvenAfterUiAlreadyShowedExpired()
    {
        var clock = new Clock(); var engine = Engine(clock);
        engine.Process(Event(GameEventType.ManualTrigger));
        clock.At(8);
        Assert.AreEqual(BuffStatus.Expired, Buff(engine).Status);
        engine.Process(Event(GameEventType.ManualRefresh, 5));
        Assert.AreEqual(BuffStatus.Active, Buff(engine).Status);
        Assert.AreEqual(3d, Buff(engine).RemainingSeconds);
        clock.At(11);
        Assert.AreEqual(BuffStatus.Expired, Buff(engine).Status);
    }

    [TestMethod]
    public void OutOfOrderEventsAndPreResetEventsCannotResurrectBuff()
    {
        var clock = new Clock(); clock.At(5); var engine = Engine(clock);
        engine.Process(Event(GameEventType.ManualTrigger, 5));
        Assert.AreEqual(EventDecision.OutOfOrder, engine.Process(Event(GameEventType.ManualDeactivate, 4)).Decision);
        Assert.AreEqual(EventDecision.OutOfOrder, engine.Process(Event(GameEventType.Reset, 4, "*")).Decision);
        engine.Process(Event(GameEventType.Reset, 5, "*"));
        Assert.AreEqual(EventDecision.OutOfOrder, engine.Process(Event(GameEventType.ManualTrigger, 5)).Decision);
        Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        clock.At(6); engine.Process(Event(GameEventType.ManualTrigger, 6));
        Assert.AreEqual(BuffStatus.Active, Buff(engine).Status);
    }

    [TestMethod]
    public void PendingDoesNotDemoteActiveAndRefreshCannotActivateInactive()
    {
        var engine = Engine(new());
        engine.Process(Event(GameEventType.ManualRefresh));
        Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        engine.Process(Event(GameEventType.BuffCandidate));
        Assert.AreEqual(BuffStatus.Pending, Buff(engine).Status);
        engine.Process(Event(GameEventType.ManualTrigger));
        engine.Process(Event(GameEventType.BuffCandidate));
        Assert.AreEqual(BuffStatus.Active, Buff(engine).Status);
    }

    [TestMethod]
    public void SignalLossPoliciesPreserveStacksAndNeverInventRemainingTime()
    {
        foreach (var policy in Enum.GetValues<SignalLossPolicy>())
        {
            var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { SignalLoss = policy }));
            engine.Process(Event(GameEventType.ManualStack));
            clock.At(1); engine.Process(Event(GameEventType.SignalLost, 1));
            Assert.AreEqual(policy == SignalLossPolicy.ExpireByTimer ? BuffStatus.Active : BuffStatus.Unknown, Buff(engine).Status);
            Assert.AreEqual(1, Buff(engine).Stacks);
            if (policy != SignalLossPolicy.ExpireByTimer) Assert.AreEqual(0d, Buff(engine).RemainingSeconds);
            clock.At(8);
            Assert.AreEqual(policy == SignalLossPolicy.ExpireByTimer ? BuffStatus.Expired : BuffStatus.Unknown, Buff(engine).Status);
            engine.Process(Event(GameEventType.BuffIconDisappeared, 8));
            Assert.AreEqual(BuffStatus.Inactive, Buff(engine).Status);
        }
    }

    [TestMethod]
    public void WaitForDisappearRequiresConfirmationAfterNominalDeadline()
    {
        var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { SignalLoss = SignalLossPolicy.WaitForDisappear }));
        engine.Process(Event(GameEventType.ManualTrigger));
        clock.At(7);
        Assert.AreEqual(BuffStatus.Unknown, Buff(engine).Status);
        Assert.AreEqual(1, Buff(engine).Stacks);
        engine.Process(Event(GameEventType.BuffIconDisappeared, 7, confidence: 0.2));
        Assert.AreEqual(BuffStatus.Unknown, Buff(engine).Status);
        engine.Process(Event(GameEventType.ManualRefresh, 7));
        Assert.AreEqual(BuffStatus.Active, Buff(engine).Status);
        Assert.AreEqual(6d, Buff(engine).RemainingSeconds);
    }

    [TestMethod]
    public void DedupIsBoundedAndOldEventsRemainRejectedAfterPruning()
    {
        var clock = new Clock(); var engine = Engine(clock, capacity: 2);
        var original = Event(GameEventType.ManualStack);
        engine.Process(original);
        clock.At(1); engine.Process(Event(GameEventType.ManualStack, 1));
        Assert.AreEqual(EventDecision.CapacityExceeded, engine.Process(Event(GameEventType.ManualStack, 1)).Decision);
        Assert.AreEqual(2, engine.RememberedEventCount);
        Assert.IsTrue(engine.Process(Event(GameEventType.Reset, 1, "*")).Accepted, "Reset must remain available when dedup is full.");
        clock.At(31);
        Assert.AreEqual(EventDecision.TooOld, engine.Process(original).Decision);
        Assert.AreEqual(0, engine.RememberedEventCount);
        Assert.IsTrue(engine.Process(Event(GameEventType.ManualStack, 31)).Accepted);
        Assert.AreEqual(1, Buff(engine).Stacks);
    }

    [TestMethod]
    public void WallClockJumpsDoNotChangeTimerAndReplayIsDeterministic()
    {
        var sequence = new[] { Event(GameEventType.ManualStack), Event(GameEventType.ManualStack, 2),
            Event(GameEventType.ManualRefresh, 4), Event(GameEventType.SignalLost, 5) };
        var a = new Clock(); var b = new Clock(); var first = Engine(a); var second = Engine(b);
        foreach (var e in sequence)
        {
            a.Elapsed = b.Elapsed = e.CapturedAt;
            a.WallClock = a.WallClock.AddDays(100); b.WallClock = b.WallClock.AddDays(-100);
            Assert.AreEqual(first.Process(e), second.Process(e));
            CollectionAssert.AreEqual(first.Snapshot().Buffs.ToArray(), second.Snapshot().Buffs.ToArray());
        }
        Assert.AreEqual(5d, Buff(first).RemainingSeconds);
    }

    [TestMethod]
    public void InvalidAndAmbiguousRulesAreRejectedBeforeTracking()
    {
        var rule = new BuffRule("one", GameEventType.ManualTrigger, "buff", "buff", BuffAction.Activate);
        Assert.ThrowsException<ArgumentException>(() => PresetRules.Validate(Preset() with { Rules = [rule, rule with { Id = "two" }] }));
        Assert.ThrowsException<ArgumentException>(() => PresetRules.Validate(Preset() with { Rules = [rule with { BuffId = "missing" }] }));
        Assert.ThrowsException<ArgumentException>(() => PresetRules.Validate(Preset() with { Rules = [rule with { MinimumConfidence = double.NaN }] }));
        Assert.ThrowsException<ArgumentException>(() => PresetRules.Validate(Preset() with { Rules = [rule with { Cooldown = TimeSpan.FromMinutes(1) }] }));
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CorrelationMemoryIsBoundedAndPruningDoesNotDeleteNewerReceipt()
    {
        var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { Retrigger = RetriggerMode.Stack }), 2);
        engine.Process(Event(GameEventType.BuffIconAppeared, correlation: "same"));
        clock.At(29.9); engine.Process(Event(GameEventType.BuffIconAppeared, 29.9, correlation: "same"));
        clock.At(30);
        Assert.AreEqual(EventDecision.Cooldown, engine.Process(Event(GameEventType.BuffIconAppeared, 30, correlation: "same")).Decision);
        Assert.AreEqual(1, engine.RememberedCorrelationCount);
        clock.At(30.1);
        Assert.IsTrue(engine.Process(Event(GameEventType.BuffIconAppeared, 30.1, correlation: "other")).Accepted);
        Assert.AreEqual(2, engine.RememberedCorrelationCount);
        clock.At(61); engine.Snapshot();
        Assert.AreEqual(0, engine.RememberedCorrelationCount);
    }

    [TestMethod]
    public void LowConfidenceLossDoesNotChangeActiveOrAdvanceOrdering()
    {
        var clock = new Clock(); var engine = Engine(clock, Preset(Definition with { SignalLoss = SignalLossPolicy.KeepUnknown }));
        engine.Process(Event(GameEventType.ManualTrigger));
        clock.At(3);
        Assert.AreEqual(EventDecision.LowConfidence, engine.Process(Event(GameEventType.SignalLost, 3, confidence: 0.2)).Decision);
        Assert.AreEqual(BuffStatus.Active, Buff(engine).Status);
        Assert.IsTrue(engine.Process(Event(GameEventType.ManualRefresh, 2)).Accepted);
        Assert.AreEqual(5d, Buff(engine).RemainingSeconds);
    }

    [TestMethod]
    public void RulesCanResetOneBuffWithoutResettingOtherBuffs()
    {
        var preset = new CharacterPreset("character", 1, [Definition, Definition with { Id = "second" }],
            [new("activate-a", GameEventType.ManualTrigger, "both", "buff", BuffAction.Activate),
             new("activate-b", GameEventType.ManualTrigger, "both", "second", BuffAction.Activate),
             new("reset-a", GameEventType.ManualDeactivate, "buff", "buff", BuffAction.Reset)]);
        var engine = Engine(new(), preset);
        Assert.AreEqual(2, engine.Process(Event(GameEventType.ManualTrigger, subject: "both")).ChangedBuffs);
        engine.Process(Event(GameEventType.ManualDeactivate));
        Assert.AreEqual(BuffStatus.Inactive, engine.Snapshot().Buffs[0].Status);
        Assert.AreEqual(BuffStatus.Active, engine.Snapshot().Buffs[1].Status);
    }

    [TestMethod, TestCategory("Benchmark")]
    public void BenchmarkOneHundredThousandEvents()
    {
        const int count = 100_000;
        var clock = new Clock(); var engine = Engine(clock);
        // Pre-generate inputs: measurement covers engine processing/projection, not Guid creation.
        var events = Enumerable.Range(0, count).Select(i => Event(GameEventType.ManualStack, i * 0.01)).ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var processed = 0;
        foreach (var e in events)
        {
            clock.Elapsed = e.CapturedAt;
            if (engine.Process(e).Accepted) processed++;
            if (processed % 100 == 0) engine.Snapshot();
        }
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(count, processed);
        Assert.IsTrue(engine.RememberedEventCount <= 4096 && engine.RememberedCorrelationCount <= 4096);
        Assert.AreEqual(3, Buff(engine).Stacks);
        TestContext.WriteLine($"Events={count}; elapsedMs={watch.Elapsed.TotalMilliseconds:F2}; eventsPerSecond={count / watch.Elapsed.TotalSeconds:F0}; allocatedBytesPerEvent={(double)bytes / count:F1}; dedupEntries={engine.RememberedEventCount}");
    }
}
