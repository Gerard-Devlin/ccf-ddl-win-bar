using DynamicWin.Utils;
using SkiaSharp;

namespace CcfDeadlineIsland.DynamicWinHost;

// Host sizing choices informed by Apple's concentric-container guidance.
// These DIP values adapt that relationship to this Windows island, not an Apple preset.
internal static class IslandDesign
{
    public const float ShellRadius = 35;
    public const float ContentInset = 16;
    public const float CardRadius = ShellRadius - ContentInset;
    public const float ControlRadius = 10;
    public const float BadgeRadius = 6;
    public const float ContentPadding = 14;

    public static SKRoundRect RoundedRect(Vec2 position, Vec2 size, float radius)
        => new(SKRect.Create(position.X, position.Y, size.X, size.Y),
            Math.Min(radius, Math.Min(size.X, size.Y) / 2));
}
