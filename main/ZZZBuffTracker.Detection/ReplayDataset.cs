using System.Runtime.CompilerServices;
using System.Text.Json;
using ZZZBuffTracker.Application;

namespace ZZZBuffTracker.Detection;

public sealed record ReplayEntry(string File, double OffsetMilliseconds, string? Expected = null);
public sealed record ReplayManifest(int SchemaVersion, string Description, string TemplatePack, ReplayEntry[] Frames);

public static class ReplayDataset
{
    public const long MaximumBytes = 128L * 1024 * 1024;
    public static async Task SaveAsync(string directory, TemplatePack pack, IReadOnlyList<GrayFrame> frames, CancellationToken token = default)
    {
        if (frames.Count is < 1 or > 300 || frames.Sum(f => (long)f.Pixels.Length) > MaximumBytes)
            throw new ArgumentException("Replay requires 1–300 frames, maximum 128 MB.");
        foreach (var frame in frames) frame.Validate();
        if (Directory.Exists(directory)) throw new IOException("Use a new dataset directory.");
        Directory.CreateDirectory(directory);
        await TemplateFiles.SaveNewAsync(Path.Combine(directory, "templates.json"), pack, token);
        List<ReplayEntry> entries = [];
        for (var i = 0; i < frames.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var frame = frames[i];
            var offset = (frame.CapturedAt - frames[0].CapturedAt).TotalMilliseconds;
            if (i > 0 && offset <= entries[^1].OffsetMilliseconds) throw new ArgumentException("Replay timestamps must increase.");
            var filename = $"frame-{i:D4}.gray";
            await using var stream = new FileStream(Path.Combine(directory, filename), FileMode.CreateNew);
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(0x315A5A47); writer.Write(frame.Width); writer.Write(frame.Height);
            await stream.WriteAsync(frame.Pixels, token);
            entries.Add(new(filename, offset));
        }
        var manifest = new ReplayManifest(1, "Recorded Gray8 frames; Expected labels must be reviewed manually.", "templates.json", entries.ToArray());
        await File.WriteAllTextAsync(Path.Combine(directory, "replay.json"), JsonSerializer.Serialize(manifest, TemplateFiles.JsonOptions), token);
    }

    public static async Task<(ReplayManifest Manifest, TemplatePack Pack)> LoadAsync(string path, CancellationToken token = default)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Replay manifest too large.");
        var manifest = JsonSerializer.Deserialize<ReplayManifest>(await File.ReadAllTextAsync(path, token), TemplateFiles.JsonOptions)
            ?? throw new InvalidDataException("Missing replay manifest.");
        if (manifest.SchemaVersion != 1 || manifest.Frames is null || manifest.Frames.Length is < 1 or > 300)
            throw new InvalidDataException("Invalid replay schema/frame count.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var pack = await TemplateFiles.LoadAsync(SafePath(directory, manifest.TemplatePack), token);
        double previous = -1; long bytes = 0;
        foreach (var entry in manifest.Frames)
        {
            if (entry is null || !double.IsFinite(entry.OffsetMilliseconds) || entry.OffsetMilliseconds <= previous
                || entry.OffsetMilliseconds < 0 || entry.OffsetMilliseconds > 60_000) throw new InvalidDataException("Invalid replay timestamp.");
            previous = entry.OffsetMilliseconds;
            bytes += new FileInfo(SafePath(directory, entry.File)).Length;
            if (bytes > MaximumBytes) throw new InvalidDataException("Replay exceeds 128 MB.");
        }
        return (manifest, pack);
    }
    internal static string SafePath(string directory, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || file.Contains(':') || file is "." or "..")
            throw new InvalidDataException("Replay entries must be local filenames.");
        return Path.Combine(directory, file);
    }
    public static GrayFrame ReadFrame(string path, long sequence, TimeSpan capturedAt)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != 0x315A5A47) throw new InvalidDataException("Invalid Gray8 signature.");
        var width = reader.ReadInt32(); var height = reader.ReadInt32();
        if (width < 1 || height < 1 || width > 7680 || height > 4320 || stream.Length != 12L + (long)width * height)
            throw new InvalidDataException("Invalid Gray8 dimensions/length.");
        return new(sequence, capturedAt, width, height, reader.ReadBytes(checked(width * height)));
    }
}

public sealed class ReplayFrameSource(string manifestPath, IClock clock) : IFrameSource
{
    public async IAsyncEnumerable<GrayFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (manifest, _) = await ReplayDataset.LoadAsync(manifestPath, cancellationToken);
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var origin = clock.Elapsed;
        for (var i = 0; i < manifest.Frames.Length; i++)
        {
            var entry = manifest.Frames[i];
            var at = origin + TimeSpan.FromMilliseconds(entry.OffsetMilliseconds);
            var delay = at - clock.Elapsed;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            yield return ReplayDataset.ReadFrame(ReplayDataset.SafePath(directory, entry.File), i, at);
        }
    }
}
