using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CcfDeadlineIsland.Services;

namespace CcfDeadlineIsland;

// Exercises the real WPF layout transform and native screen coordinates without
// changing the user's Windows accessibility settings or showing a desktop window.
internal static class AccessibilityLayoutChecks
{
    public static int Run(MainWindow window)
    {
        int checks = 0;
        void Check(bool valid, string description)
        { if (!valid) throw new InvalidOperationException("Accessibility layout: " + description); checks++; }
        var renderer = window.Renderer;
        var viewport = (ScrollViewer)renderer.Parent;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(handle)!.RootVisual = window;
        try
        {
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0, 2.25 })
            {
                window.ApplyAccessibilityScale(scale);
                window.Measure(new Size(window.Width, window.Height));
                window.Arrange(new Rect(0, 0, window.Width, window.Height));
                window.UpdateLayout(); Pump();
                renderer.Menu.ShowPage("settings"); renderer.Settle(true);
                window.UpdateLayout(); Pump();
                Check(viewport.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden
                    && viewport.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "no visible scrollbars at " + scale);
                Check(renderer.ContentReady, "settings layout settles at " + scale);
                var geometry = renderer.MainIsland.GetRect().Rect;
                double center = renderer.PointToScreen(new Point(geometry.MidX, 0)).X;
                Check(Math.Abs(center - WindowPlacement.GetPrimaryDiagnostics(window).PrimaryCenterX) <= 1,
                    "native island center at " + scale);
                var control = renderer.Menu.Controls["settings.startup"];
                var bounds = control.GetInputRect().Rect;
                var point = new Point(bounds.MidX, bounds.MidY);
                var back = renderer.PointFromScreen(renderer.PointToScreen(point));
                double pixelsPerDip = WindowPlacement.GetPrimaryDiagnostics(window).Dpi / 96.0 * scale;
                Check(Math.Abs(back.X - point.X) * pixelsPerDip <= 1 && Math.Abs(back.Y - point.Y) * pixelsPerDip <= 1,
                    "screen-to-control coordinates round trip at " + scale);
                var peer = UIElementAutomationPeer.CreatePeerForElement(renderer)!;
                ((CcfDeadlineIsland.DynamicWinHost.IslandAutomationPeer)peer).Synchronize();
                peer.GetChildren().Single(p => p.GetAutomationId() == "settings.startup").SetFocus();
                window.UpdateLayout(); Pump();
                var translated = renderer.TranslatePoint(new Point(bounds.MidX, bounds.MidY), viewport);
                Check(translated.Y >= 0 && translated.Y <= viewport.ActualHeight,
                    "keyboard focus brings final switch into the viewport at " + scale);
            }
            return checks;
        }
        finally
        {
            window.ApplyAccessibilityScale(1);
            viewport.ScrollToTop();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            window.UpdateLayout();
            renderer.Input.CancelAll(); renderer.Menu.ShowPage("widgets"); renderer.Settle(true);
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
