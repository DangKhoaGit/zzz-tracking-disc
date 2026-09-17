using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;
using ZZZBuffTracker.Domain;
using ZZZBuffTracker.Infrastructure;

namespace ZZZBuffTracker.Tests;

[TestClass]
public sealed class DetectionTests
{
    private static readonly byte[] Pattern = Enumerable.Range(0, 64).Select(i => (byte)((i / 8 + i % 8) % 2 == 0 ? 255 : 0)).ToArray();
    private static readonly CharacterPreset Preset = new("demo-character", 1, [new("buff", "Buff", TimeSpan.FromSeconds(30), SignalLoss: SignalLossPolicy.KeepUnknown)]);
    private static TrackingContext Context() => new(Guid.NewGuid(), Preset);
    private static TemplatePack Pack() => new(1, "synthetic", 1, 8, 8, 1, NormalizedRect.Full,
        [new("icon", "buff", TemplateKind.Buff, 1, NormalizedRect.Full, 8, 8, Pattern, CooldownMilliseconds: 0)]);
    private static GrayFrame Frame(int sequence, byte[]? pixels = null, int size = 8) =>
        new(sequence, TimeSpan.FromMilliseconds(sequence * 100), size, size, pixels ?? Pattern);
    private static DetectionResult Process(TemplateDetector detector, TrackingContext context, int sequence, byte[]? pixels = null)
        => detector.Process(Frame(sequence, pixels), context, TimeSpan.FromMilliseconds(sequence * 100 + 10));

    [TestMethod]
    public void DebounceEmitsOneAppearanceAndOneDisappearanceWithEvidence()
    {
        var detector = new TemplateDetector(Pack()); var context = Context();
        Assert.AreEqual(0, Process(detector, context, 0).Events.Count);
        Assert.AreEqual(0, Process(detector, context, 1).Events.Count);
        var appeared = Process(detector, context, 2).Events.Single();
        Assert.AreEqual(GameEventType.BuffIconAppeared, appeared.Type);
        Assert.AreEqual(context.SessionId, appeared.SessionId); Assert.IsFalse(appeared.IsManual);
        Assert.AreEqual(1d, appeared.Evidence!.Similarity); Assert.AreEqual(2L, appeared.Evidence.FrameSequence);
        for (var i = 3; i < 10; i++) Assert.AreEqual(0, Process(detector, context, i).Events.Count);
        var absent = Pattern.Select(p => (byte)(255 - p)).ToArray();
        Assert.AreEqual(0, Process(detector, context, 10, absent).Events.Count);
        Assert.AreEqual(0, Process(detector, context, 11, absent).Events.Count);
        Assert.AreEqual(GameEventType.BuffIconDisappeared, Process(detector, context, 12, absent).Events.Single().Type);
        Assert.AreEqual(0, Process(detector, context, 13, absent).Events.Count);
    }

    [TestMethod]
    public void AmbiguityProducesUnknownAndRecoveryRequiresConfirmation()
    {
        var detector = new TemplateDetector(Pack()); var context = Context();
        for (var i = 0; i < 3; i++) Process(detector, context, i);
        var blurred = Pattern.Select(p => (byte)(p == 255 ? 210 : 45)).ToArray();
        for (var i = 3; i < 5; i++) Assert.AreEqual(0, Process(detector, context, i, blurred).Events.Count);
        Assert.AreEqual(GameEventType.SignalLost, Process(detector, context, 5, blurred).Events.Single().Type);
        Assert.AreEqual(0, Process(detector, context, 6, blurred).Events.Count);
        Process(detector, context, 7); Process(detector, context, 8);
        Assert.AreEqual(GameEventType.BuffIconAppeared, Process(detector, context, 9).Events.Single().Type);
    }

    [TestMethod]
    public void SimilarNegativeAndSingleFrameFlickerDoNotActivate()
    {
        var detector = new TemplateDetector(Pack()); var context = Context();
        var negative = Pattern.Select((p, i) => i % 4 == 0 ? (byte)(255 - p) : p).ToArray();
        for (var i = 0; i < 20; i++) Assert.IsFalse(Process(detector, context, i, i % 4 == 0 ? Pattern : negative)
            .Events.Any(e => e.Type == GameEventType.BuffIconAppeared));
    }

    [TestMethod]
    public void GapsAndDuplicateFramesCannotSatisfyConfirmation()
    {
        var detector = new TemplateDetector(Pack()); var context = Context();
        Process(detector, context, 0); Process(detector, context, 1);
        Assert.AreEqual("StaleFrame", Process(detector, context, 1).Decisions.Single().Decision);
        Assert.AreEqual(0, Process(detector, context, 10).Events.Count);
        Assert.AreEqual(0, Process(detector, context, 11).Events.Count);
        Assert.AreEqual(1, Process(detector, context, 12).Events.Count);
    }

    [TestMethod]
    public void CooldownDefersTransitionWithoutLosingIt()
    {
        var pack = Pack(); pack = pack with { Templates = [pack.Templates[0] with { CooldownMilliseconds = 500 }] };
        var detector = new TemplateDetector(pack); var context = Context();
        for (var i = 0; i < 3; i++) Process(detector, context, i);
        var absent = new byte[64];
        for (var i = 3; i < 7; i++) Assert.AreEqual(0, Process(detector, context, i, absent).Events.Count);
        Assert.AreEqual(GameEventType.BuffIconDisappeared, Process(detector, context, 7, absent).Events.Single().Type);
    }

    [TestMethod]
    public void CharacterWinnerNeedsMarginAndDoesNotRepeat()
    {
        var t = Pack().Templates[0] with { Kind = TemplateKind.Character, SubjectId = "alice" };
        var pack = Pack() with { Templates = [t, t with { Id = "other", SubjectId = "bob" }] };
        var ambiguous = new TemplateDetector(pack); var context = Context();
        for (var i = 0; i < 10; i++) Assert.AreEqual(0, Process(ambiguous, context, i).Events.Count);
        pack = pack with { Templates = [t, pack.Templates[1] with { Pixels = Pattern.Select(p => (byte)(255 - p)).ToArray() }] };
        var detector = new TemplateDetector(pack);
        Process(detector, context, 0); Process(detector, context, 1);
        var e = Process(detector, context, 2).Events.Single();
        Assert.AreEqual(GameEventType.CharacterChanged, e.Type); Assert.AreEqual("alice", e.SubjectId);
        for (var i = 3; i < 8; i++) Assert.AreEqual(0, Process(detector, context, i).Events.Count);
    }

    [TestMethod]
    public void ClientCropAndNormalizedRoiMatchAt1080pAnd1440p()
    {
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440) })
        {
            var roi = new NormalizedRect(.25, .25, .25, .25);
            var pixels = new byte[width * height]; var r = roi.Pixels(width, height);
            for (var y = 0; y < r.Height; y++) for (var x = 0; x < r.Width; x++)
                pixels[(r.Y + y) * width + r.X + x] = Pattern[(y * 8 / r.Height) * 8 + x * 8 / r.Width];
            var frame = new GrayFrame(0, TimeSpan.Zero, width, height, pixels);
            Assert.AreEqual(1d, TemplateDetector.Match(frame, Pack().Templates[0] with { Roi = roi }));
            var cropped = frame.Crop(roi); Assert.AreEqual(r.Width, cropped.Width); Assert.AreEqual(r.Height, cropped.Height);
        }
    }

    [TestMethod]
    public void ResizeAndAspectChangeFailClosed()
    {
        var detector = new TemplateDetector(Pack()); var context = Context(); Process(detector, context, 0);
        Assert.ThrowsException<InvalidOperationException>(() => detector.Process(Frame(1, new byte[256], 16), context, TimeSpan.FromSeconds(1)));
        Assert.ThrowsException<InvalidOperationException>(() => new TemplateDetector(Pack()).Process(new(1, TimeSpan.Zero, 16, 8, new byte[128]), context, TimeSpan.Zero));
    }

    [TestMethod]
    public void InvalidAssetsRoiAndDuplicateSubjectsAreRejected()
    {
        var pack = Pack(); var t = pack.Templates[0];
        Assert.ThrowsException<ArgumentException>(() => (pack with { SchemaVersion = 2 }).Validate());
        Assert.ThrowsException<ArgumentException>(() => (pack with { Templates = [t with { Pixels = new byte[64] }] }).Validate());
        Assert.ThrowsException<ArgumentException>(() => (pack with { Templates = [t, t with { Id = "duplicate-subject" }] }).Validate());
        Assert.ThrowsException<ArgumentException>(() => (pack with { Templates = [t with { OnThreshold = double.NaN }] }).Validate());
        Assert.ThrowsException<ArgumentException>(() => new NormalizedRect(0, 0, 2, 1).Validate());
        Assert.ThrowsException<ArgumentException>(() => Frame(1, new byte[3]).Validate());
    }

    [TestMethod]
    public async Task TemplateAndReplayRoundTripPreservePixelsAndRejectTraversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zzz-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ReplayDataset.SaveAsync(directory, Pack(), [Frame(0), Frame(1), Frame(2)]);
            var path = Path.Combine(directory, "replay.json");
            var (manifest, pack) = await ReplayDataset.LoadAsync(path);
            CollectionAssert.AreEqual(Pattern, pack.Templates[0].Pixels);
            var source = new ReplayFrameSource(path, new MonotonicClock()); int count = 0;
            await foreach (var frame in source.ReadFramesAsync(CancellationToken.None)) { CollectionAssert.AreEqual(Pattern, frame.Pixels); count++; }
            Assert.AreEqual(3, count);
            await Assert.ThrowsExceptionAsync<IOException>(() => TemplateFiles.SaveNewAsync(Path.Combine(directory, "templates.json"), Pack()));
            manifest.Frames[0] = manifest.Frames[0] with { File = "../outside.gray" };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, TemplateFiles.JsonOptions));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ReplayDataset.LoadAsync(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CaptureFailureKeepsManualPipelineAliveAndSignalsUnknown()
    {
        var clock = new MonotonicClock(); var source = new CaptureEventSource(clock, new TestLog());
        var frames = new ControlledFrames(); source.Configure(frames, Pack());
        await using var service = new TrackingService(source, new BuffStateStore(), clock, new TestLog());
        await service.StartAsync(Preset); await Until(() => source.TrySend(GameEventType.ManualTrigger, "buff"));
        await Until(() => service.Snapshot.Buffs.FirstOrDefault()?.Status == BuffStatus.Active);
        frames.Fail();
        await Until(() => source.Diagnostics.Status == CaptureStatus.CaptureUnavailable);
        await Until(() => service.Snapshot.Buffs[0].Status == BuffStatus.Unknown);
        Assert.IsTrue(source.TrySend(GameEventType.ManualRefresh, "buff"));
        await Until(() => service.Snapshot.Buffs[0].Status == BuffStatus.Active);
        Assert.AreNotEqual(TrackingStatus.Faulted, service.Snapshot.Status);
    }

    [TestMethod]
    public async Task PauseReleasesFrameReaderAndResumeAndSwitchCreateFreshDetection()
    {
        var clock = new MonotonicClock(); var source = new CaptureEventSource(clock, new TestLog());
        var frames = new ControlledFrames(); source.Configure(frames, Pack());
        await using var service = new TrackingService(source, new BuffStateStore(), clock, new TestLog());
        await service.StartAsync(Preset); await Until(() => frames.Readers == 1);
        service.SetDetectionEnabled(false); await Until(() => frames.Readers == 0 && source.Diagnostics.Status == CaptureStatus.Paused);
        Assert.IsTrue(service.SubmitManual(GameEventType.ManualTrigger, "buff"));
        await Until(() => service.Snapshot.Buffs[0].Status == BuffStatus.Active);
        service.SetDetectionEnabled(true); await Until(() => frames.Starts == 2);
        await service.SwitchPresetAsync(Preset with { Version = 2 }); await Until(() => frames.Starts == 3);
        Assert.AreEqual(1, frames.MaxReaders);
        await service.StopAsync(); Assert.AreEqual(0, frames.Readers);
    }

    [TestMethod]
    public async Task AutomaticCharacterAndBuffEventsReachRepositoryAndRuleEngine()
    {
        var clock = new MonotonicClock(); var source = new CaptureEventSource(clock, new TestLog());
        var buff = Pack().Templates[0] with { CharacterId = "detected-character" };
        var character = buff with { Id = "portrait", Kind = TemplateKind.Character, SubjectId = "detected-character", CharacterId = null };
        source.Configure(new ContinuousFrames(clock), Pack() with { Templates = [character, buff] });
        await using var service = new TrackingService(source, new BuffStateStore(), clock, new TestLog(), new MemoryPresets());
        await service.StartAsync(Preset);
        var initialSession = service.Snapshot.SessionId;
        await Until(() => service.Snapshot.CharacterId == "detected-character" && service.Snapshot.Buffs.FirstOrDefault()?.Status == BuffStatus.Active);
        Assert.AreNotEqual(initialSession, service.Snapshot.SessionId);
        Assert.AreEqual("buff", service.Snapshot.Buffs.Single().Id);
        Assert.AreEqual(1L, service.Snapshot.ProcessedEvents);
    }

    [TestMethod]
    public async Task ReplayEvaluationDistinguishesUnlabelledFramesAndCountsFalseResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zzz-eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ReplayDataset.SaveAsync(directory, Pack(), [Frame(0), Frame(1), Frame(2), Frame(3)]);
            var path = Path.Combine(directory, "replay.json");
            var (manifest, _) = await ReplayDataset.LoadAsync(path);
            var unlabelled = await ReplayEvaluator.EvaluateAsync(path, Preset.CharacterId);
            Assert.AreEqual(0, unlabelled.LabelledFrames);
            for (var i = 0; i < manifest.Frames.Length; i++) manifest.Frames[i] = manifest.Frames[i] with { Expected = i == 2 ? "BuffIconAppeared:buff" : "" };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, TemplateFiles.JsonOptions));
            var good = await ReplayEvaluator.EvaluateAsync(path, Preset.CharacterId);
            Assert.AreEqual(4, good.LabelledFrames); Assert.AreEqual(1, good.TruePositives);
            Assert.AreEqual(0, good.FalsePositives + good.FalseNegatives);
            manifest.Frames[2] = manifest.Frames[2] with { Expected = "BuffIconDisappeared:buff" };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, TemplateFiles.JsonOptions));
            var bad = await ReplayEvaluator.EvaluateAsync(path, Preset.CharacterId);
            Assert.AreEqual(1, bad.FalsePositives); Assert.AreEqual(1, bad.FalseNegatives); Assert.AreEqual(1, bad.Mismatches.Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task FrameQueueDropsOldFramesButBoundsMemory()
    {
        var clock = new MonotonicClock(); var source = new CaptureEventSource(clock, new TestLog());
        source.Configure(new BurstFrames(clock), Pack());
        await using var service = new TrackingService(source, new BuffStateStore(), clock, new TestLog());
        await service.StartAsync(Preset);
        await Until(() => source.Diagnostics.Status == CaptureStatus.ReplayComplete);
        Assert.IsTrue(source.Diagnostics.DroppedFrames >= 998);
        Assert.IsTrue(source.Diagnostics.Frames <= 2);
        Assert.IsTrue(service.SubmitManual(GameEventType.ManualTrigger, "buff"));
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class TestLog : ITrackingLog
    {
        public void Write(string level, string message, Guid sessionId, Guid? eventId = null, Exception? exception = null) { }
    }
    private sealed class ControlledFrames : IFrameSource
    {
        private readonly Channel<GrayFrame> frames = Channel.CreateUnbounded<GrayFrame>();
        public int Readers, Starts, MaxReaders;
        public void Fail() => frames.Writer.TryComplete(new IOException("Lost capture target"));
        public async IAsyncEnumerable<GrayFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts); var readers = Interlocked.Increment(ref Readers); MaxReaders = Math.Max(MaxReaders, readers);
            try { await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken)) yield return frame; }
            finally { Interlocked.Decrement(ref Readers); }
        }
    }
    private sealed class BurstFrames(IClock clock) : IFrameSource
    {
        public async IAsyncEnumerable<GrayFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            for (var i = 0; i < 1000; i++) { cancellationToken.ThrowIfCancellationRequested(); yield return new(i, clock.Elapsed, 8, 8, Pattern); }
        }
    }
    private sealed class ContinuousFrames(IClock clock) : IFrameSource
    {
        public async IAsyncEnumerable<GrayFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long i = 0;
            while (true) { await Task.Delay(40, cancellationToken); yield return new(i++, clock.Elapsed, 8, 8, Pattern); }
        }
    }
    private sealed class MemoryPresets : IPresetRepository
    {
        public Task<CharacterPreset?> FindAsync(string characterId, CancellationToken cancellationToken) =>
            Task.FromResult<CharacterPreset?>(Preset with { CharacterId = characterId, Version = 2 });
        public Task SaveAsync(CharacterPreset preset, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
