using System.Collections.Immutable;

namespace ZZZBuffTracker.Domain;

public sealed record CharacterDefinition(string Id, string Name);
public sealed record DriveDiscSet(string Id, string Name);
public sealed record ProfileDocument
{
    public int SchemaVersion { get; init; } = 2;
    public long Revision { get; init; }
    public ImmutableArray<CharacterDefinition> Characters { get; init; } = [];
    public ImmutableArray<DriveDiscSet> DriveDiscSets { get; init; } = [];
    public ImmutableArray<CharacterPreset> Presets { get; init; } = [];
    public OverlaySettings Overlay { get; init; } = new();

    public ProfileDocument Validate()
    {
        if (SchemaVersion != 2) throw new NotSupportedException($"Profile schema {SchemaVersion} is not supported.");
        if (Revision < 0 || Characters.IsDefault || DriveDiscSets.IsDefault || Presets.IsDefault || Overlay is null)
            throw new ArgumentException("Profile is missing required fields.");
        Overlay.Validate();
        var characters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in Characters)
            if (character is null || string.IsNullOrWhiteSpace(character.Id) || string.IsNullOrWhiteSpace(character.Name) || !characters.Add(character.Id))
                throw new ArgumentException("Character IDs/names must be non-empty and IDs unique.");
        var discs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var disc in DriveDiscSets)
            if (disc is null || string.IsNullOrWhiteSpace(disc.Id) || string.IsNullOrWhiteSpace(disc.Name) || !discs.Add(disc.Id))
                throw new ArgumentException("Drive disc IDs/names must be non-empty and IDs unique.");
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in Presets)
        {
            PresetRules.Validate(preset);
            if (!characters.Contains(preset.CharacterId) || !mapped.Add(preset.CharacterId))
                throw new ArgumentException("Each preset must map to one declared character, without duplicates.");
            foreach (var buff in preset.Buffs)
                if (buff.DriveDiscSetId is { } id && !discs.Contains(id))
                    throw new ArgumentException($"Buff {buff.Id} references unknown drive disc {id}.");
        }
        return this;
    }

    public static ProfileDocument Demo => new()
    {
        Characters = [new("demo-character", "Nhân vật minh họa")],
        DriveDiscSets = [new("demo-disc", "Bộ đĩa minh họa")],
        Presets = [new("demo-character", 1,
            [new("demo-buff", "Buff minh họa · 6 giây", TimeSpan.FromSeconds(6), 3,
                SignalLoss: SignalLossPolicy.KeepUnknown, DriveDiscSetId: "demo-disc")])]
    };
}
