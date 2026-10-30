using System;

namespace CcfDeadlineIsland.Services.Placement;

/// <summary>Device-pixel rectangle in the virtual desktop coordinate system.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public double CenterX => Left + Width / 2.0;
}

public readonly record struct PixelPosition(int X, int Y);

public sealed record PlacementDiagnostics(
    PixelRect Monitor,
    PixelRect WorkArea,
    PixelRect Window,
    PixelRect ClientScreen,
    uint Dpi,
    PixelPosition ExpectedWindowPosition)
{
    public double PrimaryCenterX => Monitor.CenterX;
    public double ClientCenterX => ClientScreen.CenterX;
    public double CenterDeviationPixels => Math.Abs(ClientCenterX - PrimaryCenterX);
    public int ExpectedClientTop => Math.Max(Monitor.Top, WorkArea.Top);
    public int TopDeviationPixels => Math.Abs(ClientScreen.Top - ExpectedClientTop);
}

/// <summary>
/// Centers the actual render/client surface, including its offset inside the HWND.
/// Every argument is already in physical pixels: no DPI scaling of absolute
/// virtual-screen coordinates is performed here.
/// </summary>
public static class PrimaryPlacement
{
    public static PixelPosition Calculate(PixelRect monitor, PixelRect workArea, PixelRect window, PixelRect clientScreen)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0) throw new ArgumentOutOfRangeException(nameof(monitor));
        if (window.Width <= 0 || window.Height <= 0) throw new ArgumentOutOfRangeException(nameof(window));
        if (clientScreen.Width <= 0 || clientScreen.Height <= 0) throw new ArgumentOutOfRangeException(nameof(clientScreen));

        int horizontalMove = checked((int)Math.Round(monitor.CenterX - clientScreen.CenterX, MidpointRounding.AwayFromZero));
        // Side taskbars must not shift the horizontal center. Only a top taskbar
        // changes the vertical anchor.
        int top = Math.Max(monitor.Top, workArea.Top);
        return new PixelPosition(checked(window.Left + horizontalMove), checked(window.Top + top - clientScreen.Top));
    }
}
