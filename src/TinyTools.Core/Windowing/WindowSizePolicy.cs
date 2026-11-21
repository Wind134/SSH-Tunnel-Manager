namespace TinyTools.Core.Windowing;

public readonly record struct LogicalWindowSize(double Width, double Height);

/// <summary>
/// 窗口逻辑尺寸（DIP）约束策略，适配不同分辨率与 DPI 缩放。
/// </summary>
public static class WindowSizePolicy
{
    public const double DefaultWidth = 1080;
    public const double DefaultHeight = 680;
    public const double MinimumWidth = 760;
    public const double MinimumHeight = 520;
    public const double WorkAreaMargin = 24;

    /// <summary>
    /// 结合当前工作区尺寸，规范化记忆的窗口大小。
    /// </summary>
    public static LogicalWindowSize Normalize(
        double rememberedWidth,
        double rememberedHeight,
        double availableWidth,
        double availableHeight)
    {
        double maximumWidth = Math.Max(1, availableWidth - WorkAreaMargin);
        double maximumHeight = Math.Max(1, availableHeight - WorkAreaMargin);
        LogicalWindowSize minimum = GetEffectiveMinimumSize(availableWidth, availableHeight);

        double width = IsUsable(rememberedWidth) ? rememberedWidth : DefaultWidth;
        double height = IsUsable(rememberedHeight) ? rememberedHeight : DefaultHeight;

        return new LogicalWindowSize(
            Math.Clamp(width, minimum.Width, maximumWidth),
            Math.Clamp(height, minimum.Height, maximumHeight));
    }

    /// <summary>
    /// 计算在给定工作区下实际允许的最小逻辑尺寸。
    /// </summary>
    public static LogicalWindowSize GetEffectiveMinimumSize(
        double availableWidth,
        double availableHeight)
    {
        double maximumWidth = Math.Max(1, availableWidth - WorkAreaMargin);
        double maximumHeight = Math.Max(1, availableHeight - WorkAreaMargin);
        return new LogicalWindowSize(
            Math.Min(MinimumWidth, maximumWidth),
            Math.Min(MinimumHeight, maximumHeight));
    }

    private static bool IsUsable(double value)
        => double.IsFinite(value) && value > 0;
}
