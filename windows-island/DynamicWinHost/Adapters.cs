// Host adapters for the unchanged DynamicWin UI sources (main 9f24d17).
using CcfDeadlineIsland;
using CcfDeadlineIsland.DynamicWinHost;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;
using System.IO;
using System.Windows;

namespace DynamicWin.Main
{
    public static class Settings
    {
        public static bool AllowBlur { get; set; } = true;
        public static bool AllowAnimation { get; set; } = true;
        public static bool AntiAliasing { get; set; } = true;
        public static IslandObject.IslandMode IslandMode { get; set; } = IslandObject.IslandMode.Notch;
    }
    public static class MainForm
    {
        public static MainWindow Instance => (MainWindow)Application.Current.MainWindow;
    }
}

namespace DynamicWin.UI.Menu
{
    public sealed class MenuManager
    {
        public static MenuManager Instance { get; } = new();
        public IIslandMenu ActiveMenu { get; set; } = null!;
    }
}

namespace DynamicWin.Utils
{
    // These are the original Theme.UpdateTheme() dark-theme values.
    public static class Theme
    {
        public static Col TextMain { get; } = Col.FromHex("#ffffff");
        public static Col TextSecond { get; } = Col.FromHex("#a6a6a6");
        public static Col TextThird { get; } = Col.FromHex("#595959");
        public static Col Primary { get; } = Col.FromHex("#6988b7");
        public static Col Secondary { get; } = Col.FromHex("#061122");
        public static Col IslandBackground { get; } = Col.FromHex("#000000");
        public static Col IconColor { get; } = Col.FromHex("#ffffff");
        public static Col WidgetBackground { get; } = Col.FromHex("#11ffffff");
        public static Col Success { get; } = Col.FromHex("#bad844");
        public static Col Error { get; } = Col.FromHex("#d84444");
    }
}

namespace DynamicWin.Resources
{
    public static class Res
    {
        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Upstream", "DynamicWin", "Resources");
        private static readonly Dictionary<string, SKBitmap> Images = new();
        public static SKTypeface InterRegular { get; } = SKTypeface.FromFile(Path.Combine(Root, "Inter_24pt-Regular.ttf"));
        public static SKTypeface InterBold { get; } = SKTypeface.FromFile(Path.Combine(Root, "Inter_24pt-ExtraBold.ttf"));
        public static SKTypeface CascadiaMono { get; } = SKTypeface.FromFile(Path.Combine(Root, "CascadiaMono.ttf"));
        public static SKBitmap LoadImg(string relative)
        {
            if (!Images.TryGetValue(relative, out var bitmap))
            {
                bitmap = SKBitmap.Decode(Path.Combine(Root, "icons", relative.Replace('\\', Path.DirectorySeparatorChar)))
                    ?? throw new FileNotFoundException("Missing original DynamicWin icon", relative);
                Images.Add(relative, bitmap);
            }
            return bitmap;
        }
        public static SKBitmap Next => LoadImg("playback/Next.png");
        public static SKBitmap Previous => LoadImg("playback/Previous.png");
        public static SKBitmap PlayPause => LoadImg("playback/PlayPause.png");
        public static SKBitmap Settings => LoadImg("home/Settings.png");
        public static SKBitmap Tray => LoadImg("home/Tray.png");
        public static SKBitmap Widgets => LoadImg("home/Widgets.png");
        public static SKBitmap FileIcon => LoadImg("home/File.png");
        public static SKBitmap PlaceItem => LoadImg("home/PlaceItem.png");
        public static SKBitmap Check => LoadImg("home/Check.png");
        public static SKBitmap ArrowUp => LoadImg("home/ArrowUp.png");
        public static SKBitmap ArrowDown => LoadImg("home/ArrowDown.png");
    }
}
