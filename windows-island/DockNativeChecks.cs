using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CcfDeadlineIsland.Services;
using CcfDeadlineIsland.Services.Placement;

namespace CcfDeadlineIsland;

// Integrates the dock controller with a real, owned HWND without ever showing it.
internal static class DockNativeChecks
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    public static int Run()
    {
        int checks = 0, persisted = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException("Native dock check: " + message);
            checks++;
        }
        static int Pixels(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
        static double Scale(PlacementDiagnostics state) => state.Dpi / 96.0;
        var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        var preferences = new Preferences();
        var shape = new Rect(250, 7.5, 200, 35);
        var window = new Window
        {
            Width = 700, Height = 500, Left = 200, Top = 200,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false
        };
        DockController? dock = null;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Check(WindowPlacement.GetPrimaryDiagnostics(window).ClientScreen.Width > 0, "hidden host has native geometry for controller refresh");
            Check(!IsWindowVisible(handle), "owned dock HWND starts hidden");
            var foreground = GetForegroundWindow();
            dock = new DockController(window, preferences, () => shape, () => persisted++);
            dock.Refresh();
            var initial = WindowPlacement.GetPrimaryDiagnostics(window);
            double initialCenter = initial.ClientScreen.Left + (shape.Left + shape.Width / 2) * Scale(initial);
            Check(Math.Abs(initialCenter - initial.PrimaryCenterX) <= 1,
                "default compact island is centered on the physical primary display");

            double leftCenterY = initial.WorkArea.Top + initial.WorkArea.Height * .35;
            dock.Preview(new Point(initial.WorkArea.Left, leftCenterY), leftCenterY);
            var left = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(dock.IsPreviewing && dock.Side == "left" && preferences.DockSide == "top" && persisted == 0,
                "left preview changes the live dock without storing preferences");
            Check(Math.Abs(left.ClientScreen.Left + Pixels(shape.Left * Scale(left)) - left.WorkArea.Left) <= 1,
                "compact visible left edge is flush with the physical work area");

            shape = new Rect(190, 7.5, 320, 432);
            dock.Refresh();
            var expandedLeft = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(Math.Abs(expandedLeft.ClientScreen.Left + Pixels(shape.Left * Scale(expandedLeft)) - expandedLeft.WorkArea.Left) <= 1,
                "expanding the island preserves the flush left edge");
            dock.Cancel();
            var cancelled = WindowPlacement.GetPrimaryDiagnostics(window);
            double cancelledCenter = cancelled.ClientScreen.Left + (shape.Left + shape.Width / 2) * Scale(cancelled);
            Check(!dock.IsPreviewing && dock.Side == "top" && preferences.DockSide == "top" && persisted == 0,
                "cancelling the preview restores the stored dock without saving");
            Check(Math.Abs(cancelledCenter - cancelled.PrimaryCenterX) <= 1,
                "cancelled expanded island returns to the physical primary center");

            double rightCenterY = cancelled.WorkArea.Top + cancelled.WorkArea.Height * .65;
            var rightPointer = new Point(cancelled.WorkArea.Right, rightCenterY);
            dock.Commit(rightPointer, rightCenterY);
            var right = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(!dock.IsPreviewing && preferences.DockSide == "right" && Math.Abs(preferences.DockPosition - .65) < .000001 && persisted == 1,
                "committing the right dock stores its position and persists once");
            Check(Math.Abs(right.ClientScreen.Left + Pixels(shape.Right * Scale(right)) - right.WorkArea.Right) <= 1,
                "expanded visible right edge is flush with the physical work area");

            shape = new Rect(250, 7.5, 200, 35);
            dock.Refresh();
            var compactRight = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(Math.Abs(compactRight.ClientScreen.Left + Pixels(shape.Right * Scale(compactRight)) - compactRight.WorkArea.Right) <= 1,
                "contracting the island preserves the flush right edge");
            dock.Commit(rightPointer, rightCenterY);
            Check(persisted == 1, "recommitting the unchanged dock does not save again");

            dock.Preview(new Point(compactRight.WorkArea.Left, leftCenterY), leftCenterY);
            dock.Cancel();
            var restoredRight = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(preferences.DockSide == "right" && persisted == 1 && dock.Side == "right"
                    && Math.Abs(restoredRight.ClientScreen.Left + Pixels(shape.Right * Scale(restoredRight)) - restoredRight.WorkArea.Right) <= 1,
                "cancelling a later drag restores the committed right dock");
            Check(!IsWindowVisible(handle), "native docking never shows the test window");
            Check(GetForegroundWindow() == foreground, "native docking does not steal foreground focus");
            return checks;
        }
        finally
        {
            dock?.Dispose(); window.Close();
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
    }
}
