using System.IO;
using CcfDeadlineIsland.DynamicWinHost;
using DynamicWin.Main;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;

namespace CcfDeadlineIsland;

// Compare rasterized foregrounds across real hover/press frames, including fractional DPI.
// A static screenshot cannot detect glyphs changing size or position during feedback.
internal static class TextRenderingChecks
{
    public static int Run(RendererMain renderer, string directory)
    {
        var menu = renderer.Menu;
        var rows = renderer.Model.Rows;
        string page = menu.Page;
        int checks = 0;
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Text rendering: " + message);
            checks++;
        }
        try
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                foreach (string id in new[] { "filters.reset", "filters.rank.A", "filters.submission", "upcoming.row.0" })
                {
                    menu.ShowPage(id.StartsWith("upcoming", StringComparison.Ordinal) ? "widgets" : "settings");
                    renderer.Input.PointerMove(0, 490);
                    renderer.Settle(true);
                    var button = (ConferenceMenu.MenuButton)menu.Controls[id];
                    var bounds = button.GetInputRect().Rect;
                    bounds.Inflate(4, 4);
                    byte[] foreground = Raster(button, bounds, scale, hideBackground: true);
                    byte[] normal = Raster(button, bounds, scale, hideBackground: false);
                    renderer.Input.PointerMove(button.GetInputRect().Rect.MidX, button.GetInputRect().Rect.MidY);
                    bool stableHover = true, stablePress = true;
                    for (int frame = 0; frame < 30; frame++)
                    {
                        renderer.Settle(true, 1);
                        stableHover &= foreground.AsSpan().SequenceEqual(Raster(button, bounds, scale, hideBackground: true));
                    }
                    Check(stableHover, $"{id} foreground remains fixed during hover at {scale * 100}%");
                    var hit = button.GetInputRect().Rect;
                    Check(renderer.RoutePointerDown(hit.MidX, hit.MidY), id + " captures the routed press");
                    byte[] pressed = Raster(button, bounds, scale, hideBackground: false);
                    Check(!normal.AsSpan().SequenceEqual(pressed), id + " retains animated background feedback");
                    for (int frame = 0; frame < 12; frame++)
                    {
                        renderer.Settle(true, 1);
                        stablePress &= foreground.AsSpan().SequenceEqual(Raster(button, bounds, scale, hideBackground: true));
                    }
                    renderer.RoutePointerUp(0, 490); // Cancel without toggling filters or leaving the page.
                    for (int frame = 0; frame < 30; frame++)
                    {
                        renderer.Settle(true, 1);
                        stablePress &= foreground.AsSpan().SequenceEqual(Raster(button, bounds, scale, hideBackground: true));
                    }
                    Check(stablePress, $"{id} foreground remains fixed through press/release at {scale * 100}%");
                }

                var row = rows[0];
                var point = row.Deadline with { Utc = DateTimeOffset.UtcNow.AddDays(12).AddHours(12) };
                renderer.Model.Rows = new[] { row with { Conference = row.Conference with { Title = "WWWWWWWWWWWWW" }, Deadline = point } };
                menu.ShowPage("widgets");
                renderer.Settle(false);
                foreach (var label in menu.UiObjects.OfType<DWText>())
                {
                    var position = label.Position;
                    var bounds = SKRect.Create(position.X - 4, position.Y - 24, label.Size.X + 8, 64);
                    float width = label.Size.X, height = label.Size.Y;
                    byte[] reference = Raster(label, bounds, scale);
                    bool stable = true;
                    for (int frame = 0; frame < 180; frame++)
                    {
                        renderer.Settle(false, 1);
                        stable &= reference.AsSpan().SequenceEqual(Raster(label, bounds, scale));
                    }
                    Check(width == label.Size.X && height == label.Size.Y, "compact text drawing does not mutate layout metrics");
                    Check(stable, $"compact {label.Text} stays still for 180 frames at {scale * 100}%");
                }
                renderer.Model.Rows = rows;
            }
            File.WriteAllText(Path.Combine(directory, "text-rendering-checks.txt"),
                $"{checks} temporal raster checks passed at 100%, 125%, 150% and 200%. " +
                "Compared foreground pixels through routed hover/press/release and 180-frame compact idle sequences.\n");
            return checks;
        }
        finally
        {
            renderer.Input.CancelAll();
            renderer.Input.PointerMove(0, 490);
            renderer.Model.Rows = rows;
            menu.ShowPage(page);
            renderer.Settle(true);
        }
    }

    private static byte[] Raster(UIObject control, SKRect bounds, float scale, bool? hideBackground = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(bounds.Width * scale),
            (int)Math.Ceiling(bounds.Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        var color = control.Color;
        try
        {
            if (hideBackground.HasValue) control.Color = hideBackground.Value ? Col.Transparent : Col.White.Override(a: .2f);
            control.Draw(canvas);
        }
        finally { if (hideBackground.HasValue) control.Color = color; }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
