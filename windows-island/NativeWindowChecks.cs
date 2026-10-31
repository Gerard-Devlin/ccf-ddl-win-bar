using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CcfDeadlineIsland.Services;

namespace CcfDeadlineIsland;

// Exercises only an owned, hidden HWND. No desktop window is shown or activated.
internal static class NativeWindowChecks
{
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly bool SameBounds(NativeRect other) => Left == other.Left && Top == other.Top
            && Right == other.Right && Bottom == other.Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
        int width, int height, uint flags);

    public static int Run()
    {
        int checks = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException("Native topmost check: " + message);
            checks++;
        }
        bool IsTopmost(IntPtr handle) => (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExTopmost) != 0;

        var window = new Window
        {
            Width = 180, Height = 100, Left = 200, Top = 200,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false, ShowActivated = false, Topmost = false
        };
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Check(!IsWindowVisible(handle) && !IsTopmost(handle), "owned HWND starts hidden and non-topmost");
            Check(GetWindowRect(handle, out var before), "owned HWND bounds can be read");
            var foreground = GetForegroundWindow();

            Check(WindowPlacement.EnsureTopmost(window) && window.Topmost && IsTopmost(handle),
                "maintenance sets both managed and native topmost state");
            Check(!IsWindowVisible(handle), "topmost promotion does not show the hidden test window");

            const uint noSize = 0x0001, noMove = 0x0002, noActivate = 0x0010, noOwnerZOrder = 0x0200;
            Check(SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0,
                    noSize | noMove | noActivate | noOwnerZOrder) && !IsTopmost(handle),
                "external native demotion is simulated without activation");
            Check(WindowPlacement.EnsureTopmost(window) && IsTopmost(handle),
                "maintenance repairs native topmost state after demotion");
            Check(!IsWindowVisible(handle), "repair leaves the hidden test window hidden");
            Check(GetWindowRect(handle, out var after) && before.SameBounds(after),
                "topmost maintenance preserves physical position and size");
            Check(GetForegroundWindow() == foreground, "topmost maintenance does not steal foreground focus");
            return checks;
        }
        finally { window.Close(); }
    }
}
