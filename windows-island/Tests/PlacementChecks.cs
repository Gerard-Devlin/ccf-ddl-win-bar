using CcfDeadlineIsland.Services.Placement;

internal static class PlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            int width = (int)Math.Round(700 * scale), height = (int)Math.Round(500 * scale);
            var monitor = new PixelRect(0, 0, 2560, 1440);
            var workArea = new PixelRect(0, 0, 2560, 1392);
            var window = new PixelRect(137, 83, 137 + width, 83 + height);
            var position = PrimaryPlacement.Calculate(monitor, workArea, window, window);
            double resultingCenter = window.CenterX + position.X - window.Left;
            check(Math.Abs(resultingCenter - monitor.CenterX) <= 0.5, $"Physical primary centering at {scale * 100}% scaling");
            check(position.Y == 0, $"Physical top anchor at {scale * 100}% scaling");
        }

        foreach (var monitor in new[]
        {
            new PixelRect(-1920, -200, 0, 880),
            new PixelRect(0, 0, 1707, 960),
            new PixelRect(0, -1440, 2560, 0)
        })
        {
            var workArea = new PixelRect(monitor.Left + 80, monitor.Top, monitor.Right, monitor.Bottom - 48);
            var window = new PixelRect(300, 150, 1001, 650);
            var position = PrimaryPlacement.Calculate(monitor, workArea, window, window);
            check(Math.Abs(window.CenterX + position.X - window.Left - monitor.CenterX) <= 0.5,
                $"Negative/odd monitor bounds and side taskbar ({monitor.Left}, {monitor.Top})");
            check(position.Y == monitor.Top, $"Side taskbar preserves top anchor ({monitor.Left}, {monitor.Top})");
        }

        {
            var monitor = new PixelRect(0, 0, 1920, 1080);
            var workArea = new PixelRect(0, 48, 1920, 1080);
            var window = new PixelRect(100, 100, 816, 638);
            var client = new PixelRect(108, 130, 808, 630);
            var position = PrimaryPlacement.Calculate(monitor, workArea, window, client);
            check(Math.Abs(client.CenterX + position.X - window.Left - 960) <= 0.5,
                "Client-center placement compensates for invisible/nonclient borders");
            check(client.Top + position.Y - window.Top == 48, "Top taskbar is avoided including frame offset");
            var placedWindow = new PixelRect(position.X, position.Y, position.X + window.Width, position.Y + window.Height);
            var placedClient = new PixelRect(client.Left + position.X - window.Left, client.Top + position.Y - window.Top,
                client.Right + position.X - window.Left, client.Bottom + position.Y - window.Top);
            check(PrimaryPlacement.Calculate(monitor, workArea, placedWindow, placedClient) == position,
                "Already centered physical placement is idempotent");
        }
    }
}
