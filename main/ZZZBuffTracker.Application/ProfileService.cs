using System.Collections.Immutable;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Application;

public interface IProfileRepository : IPresetRepository
{
    Task<ProfileDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task<ProfileDocument> SaveProfileAsync(ProfileDocument document, long expectedRevision, CancellationToken cancellationToken = default);
    Task<ProfileDocument> ReadImportAsync(string path, CancellationToken cancellationToken = default);
    Task ExportAsync(string path, ProfileDocument document, bool overwrite, CancellationToken cancellationToken = default);
}

/// <summary>All profile mutations use revision checking; import is explicitly previewed before replacement.</summary>
public sealed class ProfileService(IProfileRepository repository)
{
    public ProfileDocument Current { get; private set; } = new();
    private bool loaded;
    public async Task ReloadAsync(CancellationToken token = default)
    {
        Current = await repository.LoadAsync(token).ConfigureAwait(false);
        loaded = true;
    }
    public async Task SavePresetAsync(CharacterDefinition character, CharacterPreset preset, long expectedRevision, CancellationToken token = default)
    {
        RequireLoaded();
        if (character.Id != preset.CharacterId) throw new ArgumentException("Character and preset IDs do not match.");
        var old = Current.Presets.FirstOrDefault(x => x.CharacterId == character.Id);
        preset = preset with { Version = checked((old?.Version ?? 0) + 1) };
        var next = Current with
        {
            Characters = Current.Characters.Where(x => x.Id != character.Id).Append(character).ToImmutableArray(),
            Presets = Current.Presets.Where(x => x.CharacterId != character.Id).Append(preset).ToImmutableArray()
        };
        Current = await repository.SaveProfileAsync(next.Validate(), expectedRevision, token).ConfigureAwait(false);
    }
    public async Task DeletePresetAsync(string characterId, long expectedRevision, CancellationToken token = default)
    {
        RequireLoaded();
        // Keep character metadata: a known character without a preset is NotConfigured.
        Current = await repository.SaveProfileAsync(Current with
        { Presets = Current.Presets.Where(x => x.CharacterId != characterId).ToImmutableArray() }, expectedRevision, token).ConfigureAwait(false);
    }
    public Task<ProfileDocument> PreviewImportAsync(string path, CancellationToken token = default) => repository.ReadImportAsync(path, token);
    public async Task ApplyImportAsync(ProfileDocument preview, long expectedRevision, CancellationToken token = default)
    {
        RequireLoaded();
        Current = await repository.SaveProfileAsync(preview.Validate(), expectedRevision, token).ConfigureAwait(false);
    }
    public Task ExportAsync(string path, OverlaySettings overlay, bool overwrite, CancellationToken token = default)
    {
        RequireLoaded();
        return repository.ExportAsync(path, (Current with { Overlay = overlay }).Validate(), overwrite, token);
    }
    public async Task SaveOverlayAsync(OverlaySettings overlay, CancellationToken token = default)
    {
        RequireLoaded();
        Current = await repository.SaveProfileAsync(Current with { Overlay = overlay.Validate() }, Current.Revision, token).ConfigureAwait(false);
    }
    private void RequireLoaded()
    {
        if (!loaded) throw new InvalidOperationException("Profile failed to load. Repair the file/backup and reload before editing.");
    }
}

public sealed class ProfileOverlaySettingsRepository(ProfileService profiles) : IOverlaySettingsRepository
{
    public Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(profiles.Current.Overlay);
    public Task SaveAsync(OverlaySettings settings, CancellationToken cancellationToken = default) => profiles.SaveOverlayAsync(settings, cancellationToken);
}
