using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZZZBuffTracker.Detection;

public sealed record NormalizedRect(double X, double Y, double Width, double Height)
{
    public static NormalizedRect Full { get; } = new(0, 0, 1, 1);
    public void Validate()
    {
        if (!double.IsFinite(X + Y + Width + Height) || X < 0 || Y < 0 || Width <= 0 || Height <= 0
            || X + Width > 1.00000001 || Y + Height > 1.00000001) throw new ArgumentException("ROI must lie inside [0,1].");
    }
    public (int X, int Y, int Width, int Height) Pixels(int width, int height)
    {
        Validate();
        var x = Math.Min(width - 1, (int)(X * width));
        var y = Math.Min(height - 1, (int)(Y * height));
        return (x, y, Math.Clamp((int)Math.Round(Width * width), 1, width - x),
            Math.Clamp((int)Math.Round(Height * height), 1, height - y));
    }
}

// Owned managed pixels: never a borrowed GPU surface; no unmanaged lifetime crosses the frame queue.
public sealed record GrayFrame(long Sequence, TimeSpan CapturedAt, int Width, int Height, byte[] Pixels)
{
    public void Validate()
    {
        if (Width < 1 || Height < 1 || Width > 7680 || Height > 4320 || Pixels is null
            || Pixels.Length != checked(Width * Height) || CapturedAt < TimeSpan.Zero || Sequence < 0)
            throw new ArgumentException("Invalid Gray8 frame (maximum 7680×4320).");
    }
    public GrayFrame Crop(NormalizedRect roi)
    {
        Validate();
        var r = roi.Pixels(Width, Height);
        var pixels = new byte[r.Width * r.Height];
        for (var y = 0; y < r.Height; y++) Pixels.AsSpan((r.Y + y) * Width + r.X, r.Width).CopyTo(pixels.AsSpan(y * r.Width));
        return new(Sequence, CapturedAt, r.Width, r.Height, pixels);
    }
}

public enum TemplateKind { Buff, Character }
public sealed record DetectionTemplate(string Id, string SubjectId, TemplateKind Kind, int Version,
    NormalizedRect Roi, int Width, int Height, byte[] Pixels,
    double OnThreshold = 0.94, double OffThreshold = 0.70, int ConfirmationFrames = 3,
    int CooldownMilliseconds = 300, string? CharacterId = null);

public sealed record TemplatePack(int SchemaVersion, string Id, int Version, int ReferenceWidth,
    int ReferenceHeight, double UiScale, NormalizedRect ClientArea, ImmutableArray<DetectionTemplate> Templates)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || Version < 1 || ReferenceWidth < 1
            || ReferenceHeight < 1 || ReferenceWidth > 7680 || ReferenceHeight > 4320
            || !double.IsFinite(UiScale) || UiScale < .5 || UiScale > 3 || ClientArea is null
            || Templates.IsDefaultOrEmpty || Templates.Length > 64) throw new ArgumentException("Invalid template pack.");
        ClientArea.Validate();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Templates)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Id) || !ids.Add(t.Id) || string.IsNullOrWhiteSpace(t.SubjectId)
                || !subjects.Add($"{t.Kind}:{t.CharacterId}:{t.SubjectId}") || !Enum.IsDefined(t.Kind) || t.Version < 1
                || t.Roi is null || t.Width < 4 || t.Height < 4 || t.Width > 128 || t.Height > 128
                || t.Pixels is null || t.Pixels.Length != t.Width * t.Height
                || !double.IsFinite(t.OnThreshold + t.OffThreshold) || t.OnThreshold < .8 || t.OnThreshold > 1
                || t.OffThreshold < 0 || t.OffThreshold >= t.OnThreshold || t.ConfirmationFrames < 2
                || t.ConfirmationFrames > 20 || t.CooldownMilliseconds < 0 || t.CooldownMilliseconds > 5000)
                throw new ArgumentException("Invalid or duplicate detection template.");
            t.Roi.Validate();
            if (t.Pixels.Max() - t.Pixels.Min() < 25) throw new ArgumentException("Template is too flat; select a distinctive icon.");
        }
    }
}

public static class TemplateFiles
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static async Task<TemplatePack> LoadAsync(string path, CancellationToken token = default)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Template pack exceeds 2 MB.");
        await using var stream = File.OpenRead(path);
        var pack = await JsonSerializer.DeserializeAsync<TemplatePack>(stream, JsonOptions, token)
            ?? throw new InvalidDataException("Empty template pack.");
        pack.Validate();
        return pack;
    }
    public static async Task SaveNewAsync(string path, TemplatePack pack, CancellationToken token = default)
    {
        pack.Validate();
        var data = JsonSerializer.SerializeToUtf8Bytes(pack, JsonOptions);
        if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("Template pack exceeds 2 MB.");
        // CreateNew deliberately preserves an existing pack/version.
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(data, token);
    }
}

public interface IFrameSource
{
    IAsyncEnumerable<GrayFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
