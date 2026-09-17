using System.Collections.Immutable;
using System.Globalization;
using ZZZBuffTracker.Domain;

namespace ZZZBuffTracker.Application;

public interface IOverlaySettingsRepository
{
    Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(OverlaySettings settings, CancellationToken cancellationToken = default);
}

public sealed class OverlaySettingsService(IOverlaySettingsRepository repository)
{
    public async Task<OverlaySettings> LoadAsync() => (await repository.LoadAsync().ConfigureAwait(false)).Validate();
    public Task SaveAsync(OverlaySettings settings) => repository.SaveAsync(settings.Validate());
}

public sealed record OverlayItem(string Id, string Name, BuffStatus Status, string Countdown,
    double Progress, int Stacks, bool IsUncertain, double Emphasis);

public static class OverlayPresentation
{
    public static ImmutableArray<OverlayItem> Project(OverlaySnapshot snapshot, bool hideInactive) =>
        snapshot.Buffs.Where(b => !hideInactive || b.Status is not (BuffStatus.Inactive or BuffStatus.Expired))
            .Select(b =>
            {
                var uncertain = b.Status is BuffStatus.Unknown or BuffStatus.Pending;
                var active = b.Status == BuffStatus.Active;
                var seconds = active && double.IsFinite(b.RemainingSeconds) ? Math.Max(0, b.RemainingSeconds) : 0;
                var progress = active && double.IsFinite(b.Progress) ? Math.Clamp(b.Progress, 0, 1) : 0;
                return new OverlayItem(b.Id, b.Name, b.Status,
                    uncertain ? "?" : seconds.ToString("F1", CultureInfo.CurrentCulture) + " s",
                    progress, Math.Max(0, b.Stacks), uncertain, active || uncertain ? 1 : 0.45);
            }).ToImmutableArray();
}
