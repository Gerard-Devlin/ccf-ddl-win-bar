using System.IO;
using System.Text.Json;
using System.Windows;
using SkiaSharp;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.Widgets;
using DynamicWin.Resources;
using DynamicWin.UI.Menu.Menus;
using CcfDeadlineIsland.Models;

namespace CcfDeadlineIsland;

// Renders the production scene to PNG without opening or interacting with a desktop window.
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
        var report = new Dictionary<string, object>();
        int checks = VerifyControls(renderer);
        foreach (string page in new[] { "widgets", "list", "details", "settings", "compact" })
        {
            renderer.Menu.ShowPage(page == "compact" ? "widgets" : page);
            renderer.Settle(page != "compact");
            using var surface = SKSurface.Create(new SKImageInfo(700, 500, SKColorType.Bgra8888, SKAlphaType.Premul));
            renderer.DrawScene(surface.Canvas);
            using var full = SKBitmap.FromImage(surface.Snapshot());
            var island = renderer.MainIsland.GetRect();
            var bounds = SKRectI.Create(Math.Max(0, (int)island.Rect.Left - 18), 0, (int)island.Width + 36, Math.Min(500, (int)island.Rect.Bottom + 18));
            using var crop = new SKBitmap();
            if (!full.ExtractSubset(crop, bounds)) throw new InvalidOperationException("Cannot crop production scene");
            using var image = SKImage.FromBitmap(crop);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(Path.Combine(directory, page + ".png"));
            encoded.SaveTo(output);
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
        File.WriteAllText(Path.Combine(directory, "checks.txt"), $"{checks} production UI checks passed. No desktop window was opened.\n");
        renderer.Dispose();
    }

    private static int VerifyControls(DynamicWin.Main.RendererMain renderer)
    {
        int count = 0;
        void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); count++; }
        IEnumerable<UIObject> All(UIObject item) => new[] { item }.Concat(item.LocalObjects.SelectMany(All));
        var menu = renderer.Menu;
        var controls = menu.UiObjects.SelectMany(All).ToList();
        renderer.Settle(true);
        Check(menu.IslandSizeBig().X == 275 && menu.IslandSizeBig().Y == 180, "Original single-card dimensions");
        var card = menu.UiObjects.OfType<WidgetBase>().First();
        Check(card.GetWidgetSize().X == 200 && card.GetWidgetSize().Y == 100 && card.roundRadius == 15, "Original card geometry");
        Check(card.LocalObjects.OfType<DWText>().Any(text => text.TextSize == 15 && text.Font == Res.InterBold), "Original title font");
        var buttons = card.LocalObjects.OfType<DWImageButton>().ToList();
        Check(buttons.Count == 3 && buttons.All(button => button.Size.X >= 29 && button.Size.X <= 31), "Original three-button sizes");
        var selected = menu.Selected!.Id;
        buttons.Single(button => button.Image.Image == Res.Next).OnMouseUp();
        Check(menu.Selected!.Id != selected, "Next conference action");
        buttons.Single(button => button.Image.Image == Res.Previous).OnMouseUp();
        Check(menu.Selected!.Id == selected, "Previous conference action");
        controls.OfType<DWTextImageButton>().Single(button => button.Text.Text == "List").OnMouseUp();
        Check(menu.Page == "list", "Original list tab interaction");
        controls.OfType<DWTextImageButton>().Single(button => button.Text.Text == "CCF").OnMouseUp();
        Check(menu.Page == "widgets", "Original main tab interaction");
        controls.OfType<DWImageButton>().Single(button => button.Image.Image == Res.Settings).OnMouseUp();
        Check(menu.Page == "settings", "Original gear interaction");
        Check(menu.IslandSizeBig().X == 530 && menu.IslandSizeBig().Y == 430, "Original settings dimensions");
        var save = controls.OfType<DWTextButton>().Single(button => button.Text.Text == "Save changes");
        Check(save.Size.X == 250 && save.Size.Y == 40 && save.roundRadius == 25 && save.Text.Font == Res.InterBold, "Original settings footer control");
        var checkbox = controls.OfType<Checkbox>().First();
        bool before = checkbox.IsChecked;
        checkbox.OnMouseUp(); Check(checkbox.IsChecked != before, "Original checkbox toggles");
        checkbox.OnMouseUp(); Check(checkbox.IsChecked == before, "Original checkbox restores");
        var rows = renderer.Model.Rows;
        var now = DateTimeOffset.UtcNow;
        var points = Enumerable.Range(0, 6).Select(index =>
        {
            var utc = now.AddDays(20 + index);
            return new DeadlinePoint("submission", utc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"), "UTC", utc, null);
        }).ToList();
        var conference = rows[0].Conference with { Id = "pagination-check", Deadlines = points };
        renderer.Model.Rows = new[] { new DeadlineRow(conference, points[0], now) };
        renderer.Model.SelectedIndex = 0;
        menu.ShowPage("details"); renderer.Settle(true);
        menu.Scroll(-120); renderer.Settle(true);
        Check(menu.UiObjects.SelectMany(All).OfType<DWText>().Any(text => text.Text.Contains(points[5].SourceTime)), "Later deadline cycles remain reachable");
        renderer.Model.Rows = rows;
        menu.ShowPage("widgets");
        return count;
    }
}
