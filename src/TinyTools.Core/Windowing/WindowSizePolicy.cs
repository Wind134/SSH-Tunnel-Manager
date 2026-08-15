namespace TinyTools.Core.Windowing;

public readonly record struct LogicalWindowSize(double Width, double Height);

/// <summary>
/// Normalizes remembered window dimensions expressed in device-independent
/// pixels (DIPs). Keeping this policy UI-neutral makes DPI and display-edge
/// cases independently testable.
/// </summary>
public static class WindowSizePolicy
{
    public const double DefaultWidth = 1120;
    public const double DefaultHeight = 720;
    public const double MinimumWidth = 960;
    public const double MinimumHeight = 620;
    public const double WorkAreaMargin = 24;

    public static LogicalWindowSize Normalize(
        double rememberedWidth,
        double rememberedHeight,
        double availableWidth,
        double availableHeight)
    {
        double maximumWidth = Math.Max(1, availableWidth - WorkAreaMargin);
        double maximumHeight = Math.Max(1, availableHeight - WorkAreaMargin);
        double minimumWidth = Math.Min(MinimumWidth, maximumWidth);
        double minimumHeight = Math.Min(MinimumHeight, maximumHeight);

        double width = IsUsable(rememberedWidth) ? rememberedWidth : DefaultWidth;
        double height = IsUsable(rememberedHeight) ? rememberedHeight : DefaultHeight;

        return new LogicalWindowSize(
            Math.Clamp(width, minimumWidth, maximumWidth),
            Math.Clamp(height, minimumHeight, maximumHeight));
    }

    private static bool IsUsable(double value)
        => double.IsFinite(value) && value > 0;
}
