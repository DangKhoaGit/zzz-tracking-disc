using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;
using ZZZBuffTracker.Domain;
using ZZZBuffTracker.Infrastructure;

namespace ZZZBuffTracker.Tests;

[TestClass]
public sealed class TrackingTests
{
    private static readonly CharacterPreset Preset = new("demo", 1,
        [new("buff", "Demo", TimeSpan.FromSeconds(6))]);

    [TestMethod]
    public async Task ManualTriggerRefreshExpiryAndResetTravelThroughPipeline()
    {
        var clock = new TestClock();
        var source = new FakeGameEventSource(clock);
        await using var service = Create(source, clock);
        await service.StartAsync(Preset);
        await Until(() => source.TryTrigger("buff"));
        await Until(() => service.Snapshot.ProcessedEvents == 1);
        var first = service.Snapshot;
        Assert.AreEqual(BuffStatus.Active, first.Buffs[0].Status);
        Assert.AreEqual(6d, first.Buffs[0].RemainingSeconds);
        clock.Advance(4);
        Assert.IsTrue(source.TryTrigger("buff"));
        await Until(() => service.Snapshot.ProcessedEvents == 2);
        Assert.AreEqual(6d, service.Snapshot.Buffs[0].RemainingSeconds);
        Assert.AreEqual(1, service.Snapshot.Buffs[0].Stacks);
        clock.Advance(7);
        await Until(() => service.Snapshot.Buffs[0].Status == BuffStatus.Expired);
        Assert.AreEqual(0d, service.Snapshot.Buffs[0].RemainingSeconds);
        Assert.AreEqual(BuffStatus.Active, first.Buffs[0].Status, "Old snapshots must remain immutable.");
        Assert.IsTrue(source.TryReset());
        await Until(() => service.Snapshot.Buffs[0].Status == BuffStatus.Inactive);
    }

    [TestMethod]
    public async Task ConcurrentStartAndRepeatedStopHaveOneReaderAndFreshSession()
    {
        var clock = new TestClock();
        var source = new ControlledSource();
        await using var service = Create(source, clock);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.StartAsync(Preset)));
        await Until(() => source.Starts == 1);
        var firstSession = service.Snapshot.SessionId;
        await Task.WhenAll(service.StopAsync(), service.StopAsync()).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, source.Readers);
        Assert.AreEqual(TrackingStatus.Stopped, service.Snapshot.Status);
        await service.StartAsync(Preset);
        await Until(() => source.Starts == 2);
        Assert.AreNotEqual(firstSession, service.Snapshot.SessionId);
        Assert.AreEqual(1, source.MaxReaders);
    }

    [TestMethod]
    public async Task RejectsOldSessionVersionLowConfidenceAndFutureTimestamp()
    {
        var clock = new TestClock();
        var source = new ControlledSource();
        await using var service = Create(source, clock);
        await service.StartAsync(Preset);
        await Until(() => source.Starts == 1);
        var valid = new GameEvent(Guid.NewGuid(), GameEventType.ManualTrigger, "buff", clock.Elapsed,
            1, "Test", "test", service.Snapshot.SessionId, 1);
        source.Send(valid with { SessionId = Guid.NewGuid() });
        source.Send(valid with { PresetVersion = 2 });
        source.Send(valid with { Confidence = 0.1 });
        source.Send(valid with { Confidence = double.NaN });
        source.Send(valid with { CapturedAt = TimeSpan.FromSeconds(100) });
        source.Send(valid);
        await Until(() => service.Snapshot.ProcessedEvents >= 1);
        Assert.AreEqual(1L, service.Snapshot.ProcessedEvents);
    }

    [TestMethod]
    public async Task LateEventDoesNotGetFreshFullDuration()
    {
        var clock = new TestClock();
        clock.Advance(10);
        var source = new ControlledSource();
        await using var service = Create(source, clock);
        await service.StartAsync(Preset);
        await Until(() => source.Starts == 1);
        source.Send(new(Guid.NewGuid(), GameEventType.ManualTrigger, "buff", TimeSpan.Zero,
            1, "Test", "late", service.Snapshot.SessionId, 1));
        await Until(() => service.Snapshot.ProcessedEvents == 1);
        Assert.AreEqual(BuffStatus.Expired, service.Snapshot.Buffs[0].Status);
    }

    [TestMethod]
    public async Task SourceFailureIsVisibleAndCanRestart()
    {
        var source = new FailingSource();
        await using var service = Create(source, new TestClock());
        await service.StartAsync(Preset);
        await Until(() => service.Snapshot.Status == TrackingStatus.Faulted);
        Assert.IsTrue(service.Snapshot.Error!.Contains("test failure"));
        await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await service.StartAsync(Preset);
        await Until(() => source.Starts == 2);
    }

    [TestMethod]
    public async Task DisposeCancelsWaitingSourceAndRejectsRestart()
    {
        var source = new ControlledSource();
        var service = Create(source, new TestClock());
        await service.StartAsync(Preset);
        await Until(() => source.Readers == 1);
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await service.DisposeAsync();
        Assert.AreEqual(0, source.Readers);
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => service.StartAsync(Preset));
    }

    [TestMethod]
    public async Task EmptyPresetWorksAndDuplicateDefinitionsAreRejected()
    {
        await using var service = Create(new ControlledSource(), new TestClock());
        await service.StartAsync(new("empty", 1, []));
        await Until(() => service.Snapshot.Status == TrackingStatus.Manual);
        Assert.AreEqual(0, service.Snapshot.Buffs.Length);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.StartAsync(
            new("bad", 1, [Preset.Buffs[0], Preset.Buffs[0]])));
    }

    [TestMethod]
    public async Task SwitchingPresetCancelsOldReaderAndRejectsQueuedOldSessionEvents()
    {
        var clock = new TestClock();
        var source = new ControlledSource();
        await using var service = Create(source, clock);
        await service.StartAsync(Preset);
        await Until(() => source.Starts == 1);
        var oldSession = service.Snapshot.SessionId;
        var next = Preset with { Version = 2, Buffs = [new("new-buff", "New", TimeSpan.FromSeconds(9))] };
        await service.SwitchPresetAsync(next).WaitAsync(TimeSpan.FromSeconds(3));
        await Until(() => source.Starts == 2);
        var newSession = service.Snapshot.SessionId;
        Assert.AreNotEqual(oldSession, newSession);
        source.Send(new(Guid.NewGuid(), GameEventType.ManualTrigger, "new-buff", clock.Elapsed, 1,
            "Test", "old-session", oldSession, 1));
        source.Send(new(Guid.NewGuid(), GameEventType.ManualTrigger, "new-buff", clock.Elapsed, 1,
            "Test", "wrong-version", newSession, 1));
        source.Send(new(Guid.NewGuid(), GameEventType.ManualTrigger, "new-buff", clock.Elapsed, 1,
            "Test", "valid", newSession, 2));
        await Until(() => service.Snapshot.ProcessedEvents == 1);
        Assert.AreEqual(2L, service.Snapshot.RejectedEvents);
        Assert.AreEqual("new-buff", service.Snapshot.Buffs.Single().Id);
        Assert.AreEqual(9d, service.Snapshot.Buffs.Single().RemainingSeconds);
        Assert.AreEqual(1, source.MaxReaders);
    }

    [TestMethod]
    public void CoreAssembliesHaveNoPresentationOrWindowsDependencies()
    {
        foreach (var assembly in new[] { typeof(GameEvent).Assembly, typeof(TrackingService).Assembly })
            foreach (var reference in assembly.GetReferencedAssemblies())
                Assert.IsFalse(reference.Name!.Contains("Presentation") || reference.Name.Contains("Windows")
                    || reference.Name.Contains("OpenCv") || reference.Name.Contains("ZZZBuffTracker.App"));
    }

    private static TrackingService Create(IGameEventSource source, IClock clock) =>
        new(source, new BuffStateStore(), clock, new TestLog());

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class TestClock : IClock
    {
        private long ticks;
        public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref ticks));
        public void Advance(int seconds) => Interlocked.Add(ref ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }
    private sealed class TestLog : ITrackingLog
    {
        public void Write(string level, string message, Guid sessionId, Guid? eventId = null, Exception? exception = null) { }
    }
    private sealed class ControlledSource : IGameEventSource
    {
        private readonly Channel<GameEvent> channel = Channel.CreateUnbounded<GameEvent>();
        public int Starts, Readers, MaxReaders;
        public void Send(GameEvent e) => channel.Writer.TryWrite(e);
        public async IAsyncEnumerable<GameEvent> ReadEventsAsync(TrackingContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var readers = Interlocked.Increment(ref Readers);
            MaxReaders = Math.Max(MaxReaders, readers);
            Interlocked.Increment(ref Starts);
            try
            {
                await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken)) yield return e;
            }
            finally { Interlocked.Decrement(ref Readers); }
        }
    }
    private sealed class FailingSource : IGameEventSource
    {
        public int Starts;
        public async IAsyncEnumerable<GameEvent> ReadEventsAsync(TrackingContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(Guid.NewGuid(), GameEventType.Reset, "*", TimeSpan.Zero, 1,
                "Test", "reset", context.SessionId, context.Preset.Version);
            throw new InvalidOperationException("test failure");
        }
    }
}
