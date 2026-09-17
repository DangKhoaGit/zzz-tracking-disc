using System.Text.Json;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Infrastructure;

public sealed class JsonOverlaySettingsRepository(string path) : IOverlaySettingsRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return new();
            await using var stream = File.OpenRead(path);
            return (await JsonSerializer.DeserializeAsync<OverlaySettings>(stream, Options, cancellationToken)
                .ConfigureAwait(false) ?? throw new JsonException("Overlay settings are empty.")).Validate();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(OverlaySettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { gate.Release(); }
        }
    }
}
