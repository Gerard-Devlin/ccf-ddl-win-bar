using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace CcfDeadlineIsland.Services;

public static class WindowPlacement
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xCCF;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    public static bool RegisterShortcut(Window window, Action toggle)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == WmHotkey && wParam.ToInt32() == HotkeyId) { toggle(); handled = true; }
            return IntPtr.Zero;
        });
        return RegisterHotKey(handle, HotkeyId, 0x0001 | 0x0002 | 0x4000, 0x44); // Ctrl+Alt+D, no repeat.
    }
    public static void UnregisterShortcut(Window window) => UnregisterHotKey(new WindowInteropHelper(window).Handle, HotkeyId);

    public static void CenterAtTop(Window window, int monitorIndex, int topOffset)
    {
        var screens = Forms.Screen.AllScreens;
        var screen = screens[Math.Clamp(monitorIndex, 0, screens.Length - 1)];
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var size = transform.Transform(new Point(screen.WorkingArea.Width, screen.WorkingArea.Height));
        window.Left = topLeft.X + (size.X - window.ActualWidth) / 2;
        window.Top = topLeft.Y + topOffset - 12;
    }

    public static bool ForegroundIsFullscreen(Window window)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == new WindowInteropHelper(window).Handle || !GetWindowRect(foreground, out var rect)) return false;
        var bounds = Forms.Screen.FromHandle(foreground).Bounds;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }
}
