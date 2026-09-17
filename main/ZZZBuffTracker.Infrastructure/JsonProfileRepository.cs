using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Infrastructure;

public sealed class JsonProfileRepository(string path, ITrackingLog log) : IProfileRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const long MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ProfileDocument document) => JsonSerializer.Serialize(Normalize(document.Validate()), Options);
    private static ProfileDocument Normalize(ProfileDocument document) => document with
    { Presets = document.Presets.Select(p => p with { Rules = PresetRules.Resolve(p) }).ToImmutableArray() };

    public static ProfileDocument Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Profile must be a JSON object.");
        var version = root["SchemaVersion"]?.GetValue<int>() ?? 1;
        if (version is not (1 or 2)) throw new NotSupportedException($"Profile schema {version} is not supported; file preserved.");
        if (root["Presets"] is not JsonArray presets) throw new JsonException("Presets array is required.");
        if (version == 1)
        {
            root["SchemaVersion"] = 2;
            root["Characters"] ??= new JsonArray(presets.Select(p => (JsonNode)new JsonObject
            { ["Id"] = p?["CharacterId"]?.GetValue<string>(), ["Name"] = p?["CharacterId"]?.GetValue<string>() }).ToArray());
            root["DriveDiscSets"] ??= new JsonArray();
            root["Overlay"] ??= JsonSerializer.SerializeToNode(new OverlaySettings(), Options);
        }
        else if (root["Characters"] is not JsonArray || root["DriveDiscSets"] is not JsonArray || root["Overlay"] is not JsonObject)
            throw new JsonException("Schema 2 requires Characters, DriveDiscSets and Overlay.");
        foreach (var node in presets)
        {
            if (node is not JsonObject preset || preset["Buffs"] is not JsonArray)
                throw new JsonException("Each preset must contain Buffs.");
            preset["Version"] ??= 1;
            // Missing Rules means compatibility defaults; [] deliberately disables all rules.
            if (preset["Rules"] is null)
            {
                preset.Remove("Rules");
                var basic = preset.Deserialize<CharacterPreset>(Options) ?? throw new JsonException("Invalid preset.");
                PresetRules.Validate(basic);
                preset["Rules"] = JsonSerializer.SerializeToNode(PresetRules.Resolve(basic), Options);
            }
        }
        return (root.Deserialize<ProfileDocument>(Options) ?? throw new JsonException("Invalid profile.")).Validate();
    }

    public async Task<ProfileDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<ProfileDocument> LoadCoreAsync(CancellationToken token)
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return Normalize(ProfileDocument.Demo);
        try { return await ReadAsync(path, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FileNotFoundException or InvalidOperationException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Profile invalid and no backup is available. " + ex.Message, ex);
            var recovered = await ReadAsync(path + ".bak", token).ConfigureAwait(false);
            var archive = path + ".corrupt-" + Guid.NewGuid().ToString("N");
            await WriteAtomicAsync(path, recovered, File.Exists(path) ? archive : null, token).ConfigureAwait(false);
            log.Write("Warning", $"Recovered profile from backup; damaged file: {archive}", Guid.Empty);
            return recovered;
        }
    }

    public async Task<ProfileDocument> SaveProfileAsync(ProfileDocument document, long expectedRevision, CancellationToken cancellationToken = default)
    {
        document.Validate();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            // Cross-process exclusion plus revision check prevents silent lost updates from two instances.
            await using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision) throw new InvalidOperationException("Profile changed. Reload before saving/importing.");
            var next = Normalize(document with { Revision = checked(current.Revision + 1) });
            await WriteAtomicAsync(path, next, path + ".bak", cancellationToken).ConfigureAwait(false);
            log.Write("Information", $"Saved profile revision={next.Revision}; backup={path}.bak", Guid.Empty);
            return next;
        }
        finally { gate.Release(); }
    }

    public async Task<CharacterPreset?> FindAsync(string characterId, CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Presets.FirstOrDefault(p => p.CharacterId == characterId);
    public async Task SaveAsync(CharacterPreset preset, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var old = current.Presets.FirstOrDefault(p => p.CharacterId == preset.CharacterId);
        var next = current with
        {
            Characters = current.Characters.Any(c => c.Id == preset.CharacterId) ? current.Characters
                : current.Characters.Add(new(preset.CharacterId, preset.CharacterId)),
            Presets = current.Presets.Where(p => p.CharacterId != preset.CharacterId)
                .Append(preset with { Version = checked((old?.Version ?? 0) + 1) }).ToImmutableArray()
        };
        await SaveProfileAsync(next, current.Revision, cancellationToken).ConfigureAwait(false);
    }
    public async Task<ProfileDocument> ReadImportAsync(string importPath, CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(importPath, cancellationToken).ConfigureAwait(false);
        log.Write("Information", $"Validated profile import: {importPath}", Guid.Empty);
        return document;
    }
    public async Task ExportAsync(string exportPath, ProfileDocument document, bool overwrite, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(exportPath);
        if (string.Equals(full, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, Path.GetFullPath(path + ".bak"), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export must not target the active profile or its backup.");
        if (File.Exists(full) && !overwrite) throw new IOException("Export file already exists. Confirm replacement first.");
        await WriteAtomicAsync(full, document, overwrite ? full + ".bak" : null, cancellationToken, !overwrite).ConfigureAwait(false);
        log.Write("Information", $"Exported profile: {full}", Guid.Empty);
    }
    private static async Task<ProfileDocument> ReadAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Profile exceeds the 2 MB limit.");
        using var reader = new StreamReader(stream);
        return Parse(await reader.ReadToEndAsync(token).ConfigureAwait(false));
    }
    private static async Task WriteAtomicAsync(string file, ProfileDocument document, string? backup, CancellationToken token, bool createOnly = false)
    {
        var json = Serialize(document);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Profile exceeds the 2 MB limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (createOnly || !File.Exists(file)) File.Move(temp, file);
            else File.Replace(temp, file, backup);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
