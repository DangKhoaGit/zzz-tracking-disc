using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;
using ZZZBuffTracker.Infrastructure;

namespace ZZZBuffTracker.Tests;

[TestClass]
public sealed class OverlayTests
{
    [TestMethod]
    public void ProjectsEmptySingleAndMultipleSnapshotsWithoutChangingSource()
    {
        Assert.AreEqual(0, OverlayPresentation.Project(OverlaySnapshot.Empty, false).Length);
        var single = OverlaySnapshot.Empty with { Buffs = [new("a", "Active", BuffStatus.Active, 3, 0.5, 2)] };
        var item = OverlayPresentation.Project(single, false).Single();
        Assert.AreEqual(2, item.Stacks);
        Assert.AreEqual(0.5, item.Progress);
        var multiple = single with { Buffs = single.Buffs.Add(new("b", "Inactive", BuffStatus.Inactive, 0, 0, 0)) };
        Assert.AreEqual(2, OverlayPresentation.Project(multiple, false).Length);
        Assert.AreEqual(1, OverlayPresentation.Project(multiple, true).Length);
        Assert.AreEqual(2, multiple.Buffs.Length);
    }

    [TestMethod]
    public void UnknownAndPendingNeverDisplayInventedCountdownAndSurviveInactiveFilter()
    {
        var snapshot = OverlaySnapshot.Empty with
        {
            Buffs = [new("u", "Unknown", BuffStatus.Unknown, 99, 0.8, 1),
                new("p", "Pending", BuffStatus.Pending, 50, 0.5, 0),
                new("e", "Expired", BuffStatus.Expired, 12, 1, 0)]
        };
        var projected = OverlayPresentation.Project(snapshot, true);
        Assert.AreEqual(2, projected.Length);
        Assert.IsTrue(projected.All(x => x.Countdown == "?" && x.Progress == 0 && x.IsUncertain));
    }

    [TestMethod]
    public void ProjectionClampsInvalidRemainingProgressAndStacks()
    {
        var snapshot = OverlaySnapshot.Empty with
        {
            Buffs = [new("a", "A", BuffStatus.Active, -3, 4, -2),
                new("b", "B", BuffStatus.Active, double.NaN, double.PositiveInfinity, 1)]
        };
        var projected = OverlayPresentation.Project(snapshot, false);
        Assert.IsTrue(projected.All(x => !x.Countdown.StartsWith('-') && !x.Countdown.Contains("NaN")));
        Assert.AreEqual(1d, projected[0].Progress);
        Assert.AreEqual(0, projected[0].Stacks);
        Assert.AreEqual(0d, projected[1].Progress);
    }

    [TestMethod]
    public void ValidatesGeometryIncludingNegativeMonitorCoordinates()
    {
        new OverlaySettings { Geometry = new(-1600, -200, 600, 300) }.Validate();
        Assert.ThrowsException<ArgumentException>(() => new OverlaySettings { Geometry = new(0, 0, 0, 0) }.Validate());
        Assert.ThrowsException<ArgumentException>(() => new OverlaySettings { Scale = double.NaN }.Validate());
        Assert.ThrowsException<ArgumentException>(() => new OverlaySettings { Opacity = 0 }.Validate());
        Assert.ThrowsException<ArgumentException>(() => new OverlaySettings { SchemaVersion = 2 }.Validate());
        Assert.ThrowsException<ArgumentException>(() => new OverlaySettings { DisplayMode = (OverlayDisplayMode)45 }.Validate());
    }

    [TestMethod]
    public async Task SettingsAndGeometryRoundTripWithBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZZZOverlayTests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "overlay.json");
            var repository = new JsonOverlaySettingsRepository(path);
            var service = new OverlaySettingsService(repository);
            Assert.AreEqual(new OverlaySettings(), await service.LoadAsync());
            var first = new OverlaySettings { Geometry = new(-100, 50, 500, 350), DisplayMode = OverlayDisplayMode.Image,
                Orientation = OverlayOrientation.Horizontal, Opacity = 0.6, Scale = 1.5, Spacing = 12, HideInactive = true };
            await service.SaveAsync(first);
            Assert.AreEqual(first, await service.LoadAsync());
            var second = first with { DisplayMode = OverlayDisplayMode.Text };
            await service.SaveAsync(second);
            Assert.AreEqual(second, await service.LoadAsync());
            Assert.AreEqual(first, await new JsonOverlaySettingsRepository(path + ".bak").LoadAsync());
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    [TestMethod]
    public async Task CorruptSettingsAreReportedAndPreservedUntilExplicitSave()
    {
        var file = Path.Combine(Path.GetTempPath(), "ZZZOverlayTests-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "{corrupt");
            var repository = new JsonOverlaySettingsRepository(file);
            await Assert.ThrowsExceptionAsync<JsonException>(() => repository.LoadAsync());
            Assert.AreEqual("{corrupt", await File.ReadAllTextAsync(file));
            await repository.SaveAsync(new());
            Assert.AreEqual("{corrupt", await File.ReadAllTextAsync(file + ".bak"));
        }
        finally { File.Delete(file); File.Delete(file + ".bak"); }
    }
}
