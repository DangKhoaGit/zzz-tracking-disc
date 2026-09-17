using System.Diagnostics;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Detection;

public sealed record ReplayEvaluation(int Frames, int LabelledFrames, int TruePositives, int FalsePositives,
    int FalseNegatives, double ProcessingMilliseconds, string[] Mismatches);

public static class ReplayEvaluator
{
    // Expected: comma-separated Type:SubjectId. Empty means no event; null means not labelled.
    // Evaluation is deterministic/offline, with no frame dropping and no live rule-state mutation.
    public static async Task<ReplayEvaluation> EvaluateAsync(string manifestPath, string characterId, CancellationToken token = default)
    {
        var (manifest, pack) = await ReplayDataset.LoadAsync(manifestPath, token);
        var detector = new TemplateDetector(pack);
        var context = new TrackingContext(Guid.NewGuid(), new CharacterPreset(characterId, 1, []));
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        int labelled = 0, tp = 0, fp = 0, fn = 0;
        double elapsed = 0; List<string> mismatches = [];
        for (var i = 0; i < manifest.Frames.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = manifest.Frames[i];
            var at = TimeSpan.FromMilliseconds(entry.OffsetMilliseconds);
            var frame = ReplayDataset.ReadFrame(ReplayDataset.SafePath(directory, entry.File), i, at);
            var started = Stopwatch.GetTimestamp();
            var events = detector.Process(frame, context, at).Events;
            elapsed += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (entry.Expected is not null)
            {
                labelled++;
                var expected = entry.Expected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                var actual = events.Select(e => $"{e.Type}:{e.SubjectId}").ToHashSet(StringComparer.Ordinal);
                tp += expected.Intersect(actual).Count(); fp += actual.Except(expected).Count(); fn += expected.Except(actual).Count();
                if (!actual.SetEquals(expected)) mismatches.Add($"{entry.File}: expected=[{string.Join(',', expected)}], actual=[{string.Join(',', actual)}]");
            }
            // Matches live session switching: start fresh detector after a different character is confirmed.
            var change = events.FirstOrDefault(e => e.Type == GameEventType.CharacterChanged && e.SubjectId != context.Preset.CharacterId);
            if (change is not null)
            {
                context = new(Guid.NewGuid(), new CharacterPreset(change.SubjectId, 1, [])); detector = new(pack);
            }
        }
        return new(manifest.Frames.Length, labelled, tp, fp, fn, elapsed, mismatches.ToArray());
    }
}
