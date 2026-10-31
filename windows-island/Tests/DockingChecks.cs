using CcfDeadlineIsland.Services.Placement;

internal static class DockingChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var monitor = new PixelRect(0, 0, 2560, 1440);
            var work = new PixelRect(72, 48, 2560, 1392);
            var window = new PixelRect(139, 117, 139 + Px(716, scale), 117 + Px(538, scale));
            var client = new PixelRect(window.Left + Px(8, scale), window.Top + Px(30, scale),
                window.Left + Px(708, scale), window.Top + Px(530, scale));
            string label = $"{scale * 100}%";

            foreach (string side in new[] { "left", "right" })
            {
                PixelRect? compact = null;
                foreach (var size in new[] { (Width: 200, Height: 35), (Width: 320, Height: 218), (Width: 320, Height: 432) })
                {
                    // The host remains wide; align the visible island, including
                    // its client offset and the native window's frame offset.
                    int left = Px((700 - size.Width) / 2.0, scale);
                    var island = new PixelRect(left, Px(4, scale), left + Px(size.Width, scale), Px(4 + size.Height, scale));
                    var centered = Place(monitor, work, window, client, island, side, .5);
                    check(Aligned(centered, work, side), $"{label} {side}: {size.Height}-DIP island edge aligns with work area");
                    check(Math.Abs(CenterY(centered) - CenterY(work)) <= .5,
                        $"{label} {side}: {size.Height}-DIP island preserves requested vertical center");
                    check(Inside(centered, work), $"{label} {side}: {size.Height}-DIP island stays inside work area");

                    if (compact is { } previous)
                        check(Math.Abs(CenterY(centered) - CenterY(previous)) <= 1,
                            $"{label} {side}: expanding to {size.Height} DIP retains center");
                    compact ??= centered;

                    var above = Place(monitor, work, window, client, island, side, -2);
                    var below = Place(monitor, work, window, client, island, side, 2);
                    check(above.Top == work.Top && Inside(above, work),
                        $"{label} {side}: {size.Height}-DIP island clamps at work-area top");
                    check(below.Bottom == work.Bottom && Inside(below, work),
                        $"{label} {side}: {size.Height}-DIP island clamps at work-area bottom");
                }
            }

            var topIsland = new PixelRect(Px(190, scale), 0, Px(510, scale), Px(218, scale));
            var top = Place(monitor, work, window, client, topIsland, "top", .9);
            check(Math.Abs(top.CenterX - monitor.CenterX) <= 1,
                $"{label} top: centers on full display despite side taskbar");
            check(top.Top == work.Top, $"{label} top: clears top taskbar despite nonclient offset");
        }

        var negativeMonitor = new PixelRect(-2560, -1440, 0, 0);
        var negativeWork = new PixelRect(-2496, -1392, 0, -48);
        var negativeWindow = new PixelRect(100, 100, 816, 638);
        var negativeClient = new PixelRect(108, 130, 808, 630);
        var negativeIsland = new PixelRect(190, 4, 510, 222);
        foreach (string side in new[] { "left", "right" })
        {
            var placed = Place(negativeMonitor, negativeWork, negativeWindow, negativeClient, negativeIsland, side, .5);
            check(Aligned(placed, negativeWork, side) && Inside(placed, negativeWork),
                $"Negative-origin {side} dock aligns actual island inside work area");
            check(Math.Abs(CenterY(placed) - CenterY(negativeWork)) <= .5,
                $"Negative-origin {side} dock preserves vertical center");
        }

        foreach (var fixture in new[]
        {
            (Monitor: new PixelRect(0, 0, 1920, 1080), Work: new PixelRect(48, 40, 1920, 1032)),
            (Monitor: negativeMonitor, Work: negativeWork)
        })
        {
            bool supported = true;
            for (int x = 0; x <= 8; x++)
                for (int y = 0; y <= 8; y++)
                {
                    double pointerX = fixture.Monitor.Left + fixture.Monitor.Width * x / 8.0;
                    double pointerY = fixture.Monitor.Top + fixture.Monitor.Height * y / 8.0;
                    supported &= DockPlacement.NearestSide(fixture.Monitor, fixture.Work, pointerX, pointerY)
                        is "top" or "left" or "right";
                }
            check(supported, $"Nearest-side grid never chooses bottom at origin {fixture.Monitor.Left},{fixture.Monitor.Top}");
            check(DockPlacement.NearestSide(fixture.Monitor, fixture.Work, fixture.Work.Left, fixture.Work.Bottom) == "left",
                "Bottom-left pointer chooses left dock");
            check(DockPlacement.NearestSide(fixture.Monitor, fixture.Work, fixture.Work.Right, fixture.Work.Bottom) == "right",
                "Bottom-right pointer chooses right dock");
            check(DockPlacement.NearestSide(fixture.Monitor, fixture.Work, fixture.Monitor.CenterX, fixture.Work.Top) == "top",
                "Top-center pointer chooses top dock");
            check(DockPlacement.ChooseSide(fixture.Monitor, fixture.Work, fixture.Work.Left + 10, fixture.Work.Top, "left", 320) == "left",
                "Dragging left dock near top corner retains side within hysteresis");
            check(DockPlacement.ChooseSide(fixture.Monitor, fixture.Work, fixture.Work.Right - 10, fixture.Work.Top, "right", 320) == "right",
                "Dragging right dock near top corner retains side within hysteresis");
            check(DockPlacement.ChooseSide(fixture.Monitor, fixture.Work, fixture.Monitor.CenterX, fixture.Work.Top, "left", 320) == "top",
                "Dragging away from side hysteresis allows top dock");
            check(DockPlacement.PositionForCenter(fixture.Work, fixture.Work.Top) == 0
                && DockPlacement.PositionForCenter(fixture.Work, fixture.Work.Bottom) == 1
                && DockPlacement.PositionForCenter(fixture.Work, CenterY(fixture.Work)) == .5,
                "Vertical position maps work-area top, center and bottom to stable preference anchors");
        }

        check(new string?[] { null, "", "bottom", "LEFT", "invalid" }.All(side => DockPlacement.NormalizeSide(side) == "top"),
            "Invalid dock sides normalize to top");
        check(new[] { "top", "left", "right" }.All(side => DockPlacement.NormalizeSide(side) == side),
            "Supported dock sides survive normalization");
        check(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }
                .All(value => DockPlacement.NormalizePosition(value) == .5),
            "Nonfinite dock positions normalize to vertical center");
        check(DockPlacement.NormalizePosition(-1) == 0 && DockPlacement.NormalizePosition(2) == 1
            && DockPlacement.NormalizePosition(.37) == .37,
            "Finite dock positions clamp outside bounds and preserve valid preference");
        check(DockPlacement.PositionForCenter(new PixelRect(0, 10, 100, 10), 999) == .5,
            "Empty work-area height uses a finite centered preference");
    }

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    private static PixelRect Place(PixelRect monitor, PixelRect work, PixelRect window, PixelRect client,
        PixelRect island, string side, double verticalPosition)
    {
        var position = DockPlacement.Calculate(monitor, work, window, client, island, side, verticalPosition);
        int x = client.Left + position.X - window.Left;
        int y = client.Top + position.Y - window.Top;
        return new PixelRect(x + island.Left, y + island.Top, x + island.Right, y + island.Bottom);
    }

    private static bool Aligned(PixelRect island, PixelRect work, string side) =>
        side == "left" ? island.Left == work.Left : island.Right == work.Right;

    private static bool Inside(PixelRect island, PixelRect work) => island.Left >= work.Left
        && island.Top >= work.Top && island.Right <= work.Right && island.Bottom <= work.Bottom;

    private static double CenterY(PixelRect rect) => rect.Top + rect.Height / 2.0;
}
