using System;

namespace CcfDeadlineIsland.Services.Placement;

/// <summary>Pure placement rules for the three supported screen-edge docks.</summary>
public static class DockPlacement
{
    public static string NormalizeSide(string? side) => side is "left" or "right" ? side : "top";

    public static double NormalizePosition(double position) => double.IsFinite(position) ? Math.Clamp(position, 0, 1) : .5;

    /// <summary>There is deliberately no bottom dock; select the nearest allowed edge.</summary>
    public static string NearestSide(PixelRect monitor, PixelRect workArea, double pointerX, double pointerY)
    {
        double topDistance = Math.Max(0, pointerY - Math.Max(monitor.Top, workArea.Top));
        double leftDistance = Math.Max(0, pointerX - workArea.Left);
        double rightDistance = Math.Max(0, workArea.Right - pointerX);
        if (topDistance <= Math.Min(leftDistance, rightDistance)) return "top";
        return leftDistance <= rightDistance ? "left" : "right";
    }

    public static string ChooseSide(PixelRect monitor, PixelRect workArea, double pointerX, double pointerY,
        string? currentSide, double islandWidth)
    {
        // Keep vertical side drags on that side even near a top corner. Moving
        // the grabbed island away from its edge releases this small hysteresis.
        double sideBand = Math.Min(workArea.Width * .45, Math.Max(40, islandWidth));
        currentSide = NormalizeSide(currentSide);
        if (currentSide == "left" && pointerX <= workArea.Left + sideBand) return "left";
        if (currentSide == "right" && pointerX >= workArea.Right - sideBand) return "right";
        return NearestSide(monitor, workArea, pointerX, pointerY);
    }

    public static double PositionForCenter(PixelRect workArea, double desiredCenterY) =>
        workArea.Height > 0 ? NormalizePosition((desiredCenterY - workArea.Top) / workArea.Height) : .5;

    /// <param name="islandInClientPixels">Visible island bounds relative to the native client origin, already scaled to device pixels.</param>
    public static PixelPosition Calculate(PixelRect monitor, PixelRect workArea, PixelRect window,
        PixelRect clientScreen, PixelRect islandInClientPixels, string? side, double verticalPosition)
    {
        side = NormalizeSide(side);
        if (side == "top") return PrimaryPlacement.Calculate(monitor, workArea, window, clientScreen);
        if (workArea.Width <= 0 || workArea.Height <= 0 || islandInClientPixels.Width <= 0 || islandInClientPixels.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(islandInClientPixels));

        double visualLeft = side == "left" ? workArea.Left : workArea.Right - islandInClientPixels.Width;
        double centerY = workArea.Top + NormalizePosition(verticalPosition) * workArea.Height;
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - islandInClientPixels.Height);
        double visualTop = Math.Clamp(centerY - islandInClientPixels.Height / 2.0, workArea.Top, maximumTop);
        int x = checked((int)Math.Round(window.Left + visualLeft - clientScreen.Left - islandInClientPixels.Left,
            MidpointRounding.AwayFromZero));
        int y = checked((int)Math.Round(window.Top + visualTop - clientScreen.Top - islandInClientPixels.Top,
            MidpointRounding.AwayFromZero));
        return new PixelPosition(x, y);
    }
}
