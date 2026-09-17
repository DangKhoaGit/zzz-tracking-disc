namespace ZZZBuffTracker.Domain;

public enum OverlayDisplayMode { Text, Image, Hybrid }
public enum OverlayOrientation { Vertical, Horizontal }

// Geometry is WPF device-independent units (DIP), never capture pixels.
public sealed record OverlayGeometry(double Left = 40, double Top = 80, double Width = 380, double Height = 260);
public sealed record OverlaySettings
{
    public int SchemaVersion { get; init; } = 1;
    public OverlayGeometry Geometry { get; init; } = new();
    public OverlayDisplayMode DisplayMode { get; init; } = OverlayDisplayMode.Hybrid;
    public OverlayOrientation Orientation { get; init; } = OverlayOrientation.Vertical;
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 0.9;
    public double Spacing { get; init; } = 8;
    public bool HideInactive { get; init; }

    public OverlaySettings Validate()
    {
        if (SchemaVersion != 1) throw new ArgumentException("Unsupported overlay settings version.");
        if (!Enum.IsDefined(DisplayMode) || !Enum.IsDefined(Orientation))
            throw new ArgumentException("Invalid overlay display mode or orientation.");
        if (!double.IsFinite(Scale) || Scale < 0.5 || Scale > 2 ||
            !double.IsFinite(Opacity) || Opacity < 0.2 || Opacity > 1 ||
            !double.IsFinite(Spacing) || Spacing < 0 || Spacing > 32)
            throw new ArgumentException("Invalid overlay scale, opacity or spacing.");
        if (Geometry is null || !double.IsFinite(Geometry.Left) || !double.IsFinite(Geometry.Top) ||
            !double.IsFinite(Geometry.Width) || !double.IsFinite(Geometry.Height) ||
            Math.Abs(Geometry.Left) > 100000 || Math.Abs(Geometry.Top) > 100000 ||
            Geometry.Width < 160 || Geometry.Width > 4000 || Geometry.Height < 100 || Geometry.Height > 4000)
            throw new ArgumentException("Invalid overlay geometry.");
        return this;
    }
}
