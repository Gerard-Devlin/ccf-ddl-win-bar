using System.IO;
using System.Text.Json;
using System.Windows;
using SkiaSharp;

namespace CcfDeadlineIsland;

// Uses the production renderer and WPF input handlers without opening a desktop window.
public static class PreviewRenderer
{
    public static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        var window = new MainWindow(preview: true, demo: true);
        System.Windows.Application.Current.MainWindow = window;
        var renderer = window.Renderer;
        renderer.Measure(new Size(700, 500));
        renderer.Arrange(new Rect(0, 0, 700, 500));
        window.PrepareOffscreenPreview();
        int checks = UiRegressionChecks.Run(renderer);
        int nativeChecks = NativeWindowChecks.Run();
        var report = new Dictionary<string, object>();
        foreach (string page in new[] { "widgets", "list", "details", "filters", "display", "compact" })
        {
            renderer.Menu.ShowPage(page == "compact" ? "widgets" : page);
            renderer.Settle(page != "compact");
            using var surface = SKSurface.Create(new SKImageInfo(700, 500, SKColorType.Bgra8888, SKAlphaType.Premul));
            renderer.DrawScene(surface.Canvas);
            using var full = SKBitmap.FromImage(surface.Snapshot());
            var island = renderer.MainIsland.GetRect();
            var bounds = SKRectI.Create(Math.Max(0, (int)island.Rect.Left - 18), 0,
                Math.Min(700, (int)island.Width + 36), Math.Min(500, (int)island.Rect.Bottom + 18));
            using var crop = new SKBitmap();
            if (!full.ExtractSubset(crop, bounds)) throw new InvalidOperationException("Cannot crop production scene");
            using var image = SKImage.FromBitmap(crop);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var output = File.Create(Path.Combine(directory, page + ".png"))) encoded.SaveTo(output);
            if (page == "widgets")
            {
                using var stage = SKSurface.Create(new SKImageInfo(crop.Width + 48, crop.Height + 32));
                stage.Canvas.Clear(new SKColor(24, 39, 58));
                stage.Canvas.DrawBitmap(crop, 24, 16);
                using var staged = stage.Snapshot();
                using var stagedData = staged.Encode(SKEncodedImageFormat.Png, 100);
                using var stagedOutput = File.Create(Path.Combine(directory, "preview.png"));
                stagedData.SaveTo(stagedOutput);
            }
            report[page] = new { width = island.Width, height = island.Height, source = "DynamicWin main 9f24d17", controls = renderer.Menu.UiObjects.Count };
        }
        File.WriteAllText(Path.Combine(directory, "geometry.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "checks.txt"), $"{checks} production UI checks passed via WPF routed pointer events and keyboard input. {nativeChecks} native topmost checks passed. No desktop window was opened.\n");
        renderer.Dispose();
    }
}
