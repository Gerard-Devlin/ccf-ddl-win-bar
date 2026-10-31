using System.IO;
using System.Text.Json;
using System.Windows;
using CcfDeadlineIsland.DynamicWinHost;
using DynamicWin.UI.UIElements;
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
        int startupChecks = StartupRegistrationChecks.Run();
        int dockingChecks = DockNativeChecks.Run();
        var report = new Dictionary<string, object>();
        var rows = renderer.Model.Rows;
        var followedIds = renderer.Model.Preferences.FollowedConferenceIds;
        var copyAction = renderer.Model.CopyTime;
        bool submissionOnly = renderer.Model.Preferences.SubmissionOnly;
        bool launchAtStartup = renderer.Model.LaunchAtStartup;
        foreach (string page in new[] { "widgets", "list", "list-two", "details", "settings", "settings-switches-on", "compact", "urgent", "urgent-compact", "urgent-details", "followed", "followed-compact", "followed-details", "left-docked", "right-docked", "left-expanded", "right-expanded", "copy-feedback" })
        {
            string side = page.StartsWith("left-", StringComparison.Ordinal) ? "left" : page.StartsWith("right-", StringComparison.Ordinal) ? "right" : "top";
            renderer.Model.DockSide = side;
            DynamicWin.Main.Settings.IslandMode = side == "top" ? IslandObject.IslandMode.Notch : IslandObject.IslandMode.Island;
            bool followed = page.StartsWith("followed", StringComparison.Ordinal) || page.EndsWith("-docked", StringComparison.Ordinal);
            bool urgent = page.StartsWith("followed", StringComparison.Ordinal) || page.StartsWith("urgent", StringComparison.Ordinal);
            renderer.Model.Preferences.SubmissionOnly = page == "settings-switches-on" || submissionOnly;
            renderer.Model.LaunchAtStartup = page == "settings-switches-on" || launchAtStartup;
            renderer.Model.Preferences.FollowedConferenceIds = followed ? new() { rows[0].Id } : followedIds;
            if (urgent)
            {
                var deadlineTime = DateTimeOffset.UtcNow.AddHours(18);
                var deadline = rows[0].Deadline with { Utc = deadlineTime,
                    SourceTime = deadlineTime.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), Timezone = "UTC" };
                var conference = rows[0].Conference with { Deadlines = new() { deadline } };
                renderer.Model.Rows = new[] { new CcfDeadlineIsland.Models.DeadlineRow(conference, deadline, DateTimeOffset.UtcNow) };
            }
            else renderer.Model.Rows = page == "list-two" ? rows.Take(2).ToArray() : rows;
            renderer.Menu.ShowPage(page switch { "compact" or "urgent" or "urgent-compact" or "followed" or "followed-compact" or "left-docked" or "right-docked" or "left-expanded" or "right-expanded" => "widgets", "settings-switches-on" => "settings", "list-two" => "list", "urgent-details" or "followed-details" or "copy-feedback" => "details", _ => page });
            renderer.Settle(page is not ("compact" or "urgent-compact" or "followed-compact" or "left-docked" or "right-docked"));
            if (page == "copy-feedback")
            {
                renderer.Model.CopyTime = () => true;
                var button = renderer.Menu.Controls["details.copy"].GetInputRect().Rect;
                renderer.RoutePointerDown(button.MidX, button.MidY); renderer.RoutePointerUp(button.MidX, button.MidY);
                renderer.Settle(true, 45);
            }
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
            if (side != "top")
            {
                const float previewScale = 1.5f;
                using var sideSurface = SKSurface.Create(new SKImageInfo(1050, 750));
                sideSurface.Canvas.Scale(previewScale);
                renderer.DrawScene(sideSurface.Canvas);
                using var sideImage = sideSurface.Snapshot();
                using var stage = SKSurface.Create(new SKImageInfo(600, 500));
                stage.Canvas.Clear(new SKColor(24, 39, 58));
                float x = side == "left" ? -island.Rect.Left * previewScale : 600 - island.Rect.Right * previewScale;
                stage.Canvas.DrawImage(sideImage, x, (500 - island.Height * previewScale) / 2 - island.Rect.Top * previewScale);
                using var staged = stage.Snapshot();
                using var stagedData = staged.Encode(SKEncodedImageFormat.Png, 100);
                using var stagedOutput = File.Create(Path.Combine(directory, page + "-preview.png"));
                stagedData.SaveTo(stagedOutput);
            }
            report[page] = new { width = island.Width, height = island.Height, source = "DynamicWin main 9f24d17", controls = renderer.Menu.UiObjects.Count };
        }
        renderer.Model.Preferences.FollowedConferenceIds = followedIds;
        renderer.Model.CopyTime = copyAction;
        renderer.Model.Preferences.SubmissionOnly = submissionOnly;
        renderer.Model.LaunchAtStartup = launchAtStartup;
        renderer.Model.DockSide = "top";
        DynamicWin.Main.Settings.IslandMode = IslandObject.IslandMode.Notch;
        File.WriteAllText(Path.Combine(directory, "geometry.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "checks.txt"), $"{checks} production UI checks passed via WPF routed pointer events and keyboard input. {nativeChecks} native topmost, {dockingChecks} native docking and {startupChecks} isolated startup registration checks passed. No desktop window was opened.\n");
        renderer.Dispose();
    }
}
