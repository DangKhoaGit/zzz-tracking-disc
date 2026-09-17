using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;
using ZZZBuffTracker.Infrastructure;
using ZZZBuffTracker.Detection;

namespace ZZZBuffTracker.Tests;

[TestClass]
public sealed class ProfileTests
{
    private string directory = null!;
    private string PathFor(string name) => Path.Combine(directory, name);
    private readonly Log log = new();
    [TestInitialize] public void Initialize() { directory = Path.Combine(Path.GetTempPath(), "ZZZProfiles-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TestCleanup] public void Cleanup() { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    private JsonProfileRepository Repository => new(PathFor("profiles.json"), log);

    [TestMethod]
    public async Task ShippedSampleProfilesValidateAndLegacyMigrates()
    {
        var current = JsonProfileRepository.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "profile-v2.json")));
        var legacy = JsonProfileRepository.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "profile-v1.json")));
        Assert.AreEqual(2, current.Presets.Length);
        Assert.AreEqual(3, current.Characters.Length);
        Assert.AreEqual(2, legacy.SchemaVersion);
        Assert.IsTrue(legacy.Presets[0].Rules.Length > 0);
    }

    [TestMethod]
    public async Task ProfileRoundTripPreservesRulesMetadataOverlayAndVersions()
    {
        var repo = Repository;
        var demo = ProfileDocument.Demo;
        var saved = await repo.SaveProfileAsync(demo with { Overlay = new() { Scale = 1.5 } }, 0);
        var loaded = await repo.LoadAsync();
        Assert.AreEqual(1L, loaded.Revision);
        Assert.AreEqual(1.5, loaded.Overlay.Scale);
        Assert.AreEqual("demo-disc", loaded.Presets[0].Buffs[0].DriveDiscSetId);
        Assert.AreEqual(JsonProfileRepository.Serialize(saved), JsonProfileRepository.Serialize(loaded));
        Assert.IsTrue(loaded.Presets[0].Rules.Length > 0);
    }

    [TestMethod]
    public void MigrationV1SuppliesDefaultsButPreservesExplicitEmptyRules()
    {
        var node = JsonNode.Parse(JsonProfileRepository.Serialize(ProfileDocument.Demo))!.AsObject();
        node["SchemaVersion"] = 1; node.Remove("Characters"); node.Remove("DriveDiscSets"); node.Remove("Overlay");
        var preset = node["Presets"]![0]!.AsObject();
        preset.Remove("Rules"); preset.Remove("Version");
        preset["Buffs"]![0]!.AsObject().Remove("DriveDiscSetId");
        preset["Buffs"]![0]!.AsObject().Remove("SignalLoss");
        var migrated = JsonProfileRepository.Parse(node.ToJsonString());
        Assert.AreEqual(2, migrated.SchemaVersion);
        Assert.AreEqual("demo-character", migrated.Characters[0].Id);
        Assert.AreEqual(1, migrated.Presets[0].Version);
        Assert.AreEqual(SignalLossPolicy.ExpireByTimer, migrated.Presets[0].Buffs[0].SignalLoss);
        Assert.IsTrue(migrated.Presets[0].Rules.Length > 0);
        preset["Rules"] = new JsonArray();
        Assert.AreEqual(0, JsonProfileRepository.Parse(node.ToJsonString()).Presets[0].Rules.Length);
    }

    [TestMethod]
    public async Task CorruptPrimaryRecoversBackupAndArchivesDamagedFile()
    {
        var repo = Repository;
        var first = await repo.SaveProfileAsync(ProfileDocument.Demo, 0);
        await repo.SaveProfileAsync(first with { Overlay = new() { Scale = 1.4 } }, first.Revision);
        await File.WriteAllTextAsync(PathFor("profiles.json"), "{broken");
        var recovered = await repo.LoadAsync();
        Assert.AreEqual(1L, recovered.Revision);
        Assert.AreEqual(1d, recovered.Overlay.Scale);
        var archive = Directory.GetFiles(directory, "*.corrupt-*").Single();
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(archive));
        Assert.IsTrue(log.Messages.Any(m => m.Contains("Recovered")));
        Assert.AreEqual(1L, (await repo.LoadAsync()).Revision);
    }

    [TestMethod]
    public async Task CorruptWithoutBackupAndFutureSchemaAreNeverSilentlyOverwritten()
    {
        var repo = Repository;
        await File.WriteAllTextAsync(PathFor("profiles.json"), "{broken");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => repo.LoadAsync());
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => repo.SaveProfileAsync(ProfileDocument.Demo, 0));
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(PathFor("profiles.json")));
        await File.WriteAllTextAsync(PathFor("profiles.json.bak"), JsonProfileRepository.Serialize(ProfileDocument.Demo));
        const string future = "{\"SchemaVersion\":99,\"Presets\":[]}";
        await File.WriteAllTextAsync(PathFor("profiles.json"), future);
        await Assert.ThrowsExceptionAsync<NotSupportedException>(() => repo.LoadAsync());
        Assert.AreEqual(future, await File.ReadAllTextAsync(PathFor("profiles.json")));
    }

    [TestMethod]
    public async Task PresetCrudIncrementsVersionAndRejectsStaleEditorRevision()
    {
        var service = new ProfileService(Repository); await service.ReloadAsync();
        var demo = service.Current.Presets[0];
        await service.SavePresetAsync(new(demo.CharacterId, "Renamed"), demo, 0);
        Assert.AreEqual(2, service.Current.Presets[0].Version);
        var oldRevision = service.Current.Revision;
        await service.SaveOverlayAsync(new() { Scale = 1.1 });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.SavePresetAsync(new(demo.CharacterId, "Lost edit"), demo, oldRevision));
        await service.ReloadAsync();
        await service.DeletePresetAsync(demo.CharacterId, service.Current.Revision);
        Assert.AreEqual(0, service.Current.Presets.Length);
        Assert.AreEqual(1, service.Current.Characters.Length);
        Assert.IsNull(await Repository.FindAsync(demo.CharacterId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ImportPreviewDoesNotMutateAndExportDoesNotOverwriteWithoutConsent()
    {
        var repo = Repository; var service = new ProfileService(repo); await service.ReloadAsync();
        var import = PathFor("import.json"); var export = PathFor("export.json");
        await File.WriteAllTextAsync(import, JsonProfileRepository.Serialize(new ProfileDocument()));
        var preview = await service.PreviewImportAsync(import);
        Assert.AreEqual(1, service.Current.Presets.Length);
        await service.ApplyImportAsync(preview, 0);
        Assert.AreEqual(0, service.Current.Presets.Length);
        await service.ExportAsync(export, new(), false);
        await Assert.ThrowsExceptionAsync<IOException>(() => service.ExportAsync(export, new(), false));
        await service.ExportAsync(export, new() { Scale = 1.2 }, true);
        Assert.IsTrue(File.Exists(export + ".bak"));
        Assert.IsTrue(log.Messages.Any(m => m.Contains("import")) && log.Messages.Any(m => m.Contains("Exported")));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ExportAsync(PathFor("profiles.json"), new(), true));
    }

    [TestMethod]
    public async Task InvalidImportAndMissingFieldsLeaveCurrentProfileIntact()
    {
        var repo = Repository; await repo.SaveProfileAsync(ProfileDocument.Demo, 0);
        var before = await File.ReadAllTextAsync(PathFor("profiles.json"));
        await File.WriteAllTextAsync(PathFor("import.json"), "{\"SchemaVersion\":2,\"Presets\":[]}");
        await Assert.ThrowsExceptionAsync<JsonException>(() => repo.ReadImportAsync(PathFor("import.json")));
        Assert.AreEqual(before, await File.ReadAllTextAsync(PathFor("profiles.json")));
        var invalid = ProfileDocument.Demo with { DriveDiscSets = [] };
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => repo.SaveProfileAsync(invalid, 1));
    }

    [TestMethod]
    public async Task WriteFailureAndCancellationPreserveOriginalAndRemoveTemporaryFiles()
    {
        var repo = Repository; var first = await repo.SaveProfileAsync(ProfileDocument.Demo, 0);
        var text = await File.ReadAllTextAsync(PathFor("profiles.json"));
        using (var locked = new FileStream(PathFor("profiles.json.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsExceptionAsync<IOException>(() => repo.SaveProfileAsync(first, first.Revision));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => repo.SaveProfileAsync(first, first.Revision, cts.Token));
        Assert.AreEqual(text, await File.ReadAllTextAsync(PathFor("profiles.json")));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        await File.WriteAllTextAsync(PathFor("not-a-directory"), "occupied");
        var bad = new JsonProfileRepository(Path.Combine(PathFor("not-a-directory"), "profile.json"), log);
        await Assert.ThrowsExceptionAsync<IOException>(() => bad.SaveProfileAsync(first, 0));
    }

    [TestMethod]
    public async Task CharacterChangedMapsPresetUnknownClearsOverlayAndManualWorksWhilePaused()
    {
        var repo = Repository;
        var document = ProfileDocument.Demo;
        document = document with { Characters = document.Characters.Add(new("second", "Second")),
            Presets = document.Presets.Add(new("second", 1, [new("second-buff", "Second", TimeSpan.FromSeconds(9))])) };
        await repo.SaveProfileAsync(document, 0);
        var clock = new MonotonicClock(); var source = new FakeGameEventSource(clock);
        await using var tracker = new TrackingService(source, new BuffStateStore(), clock, log, repo);
        await tracker.StartAsync(document.Presets[0]);
        await Until(() => tracker.SimulateCharacterChanged("second"));
        await Until(() => tracker.Snapshot.CharacterId == "second" && tracker.Snapshot.Buffs.Length == 1);
        var secondSession = tracker.Snapshot.SessionId;
        tracker.SetDetectionEnabled(false);
        Assert.IsTrue(tracker.SimulateCharacterChanged("missing"));
        Assert.IsTrue(tracker.SubmitManual(GameEventType.ManualTrigger, "second-buff"));
        await Until(() => tracker.Snapshot.ProcessedEvents == 1);
        Assert.AreEqual(secondSession, tracker.Snapshot.SessionId);
        Assert.AreEqual(BuffStatus.Active, tracker.Snapshot.Buffs[0].Status);
        tracker.SetDetectionEnabled(true);
        Assert.IsTrue(tracker.SimulateCharacterChanged("missing"));
        await Until(() => tracker.Snapshot.Status == TrackingStatus.NotConfigured);
        Assert.AreEqual(0, tracker.Snapshot.Buffs.Length);
        Assert.AreNotEqual(secondSession, tracker.Snapshot.SessionId);
        Assert.IsTrue(tracker.SimulateCharacterChanged("second"));
        await Until(() => tracker.Snapshot.CharacterId == "second" && tracker.Snapshot.Status == TrackingStatus.Manual);
        Assert.AreEqual(BuffStatus.Inactive, tracker.Snapshot.Buffs[0].Status);
    }

    private static async Task Until(Func<bool> predicate)
    { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!predicate()) await Task.Delay(10, cts.Token); }
    private sealed class Log : ITrackingLog
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Messages { get; } = [];
        public void Write(string level, string message, Guid sessionId, Guid? eventId = null, Exception? exception = null) => Messages.Add(message);
    }
}
