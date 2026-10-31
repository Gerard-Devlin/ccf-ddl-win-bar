using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CcfDeadlineIsland.Services;

namespace CcfDeadlineIsland;

// Verifies fixed primary-screen placement using an owned, hidden HWND.
internal static class DockNativeChecks
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    public static int Run()
    {
        int checks = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException("Native primary placement check: " + message);
            checks++;
        }
        var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        var window = new Window
        {
            Width = 700, Height = 500, Left = 200, Top = 200,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false
        };
        IDisposable? placement = null;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Check(WindowPlacement.GetPrimaryDiagnostics(window).ClientScreen.Width > 0,
                "owned hidden HWND has native client geometry");
            Check(!IsWindowVisible(handle), "owned test window starts hidden");
            var foreground = GetForegroundWindow();
            placement = WindowPlacement.AttachPrimary(window);
            Pump();
            var initial = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(initial.CenterDeviationPixels <= 1 && initial.TopDeviationPixels == 0,
                "attachment places the client at the primary top center");

            window.Width = 612;
            Pump();
            var resized = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(resized.ClientScreen.Width != initial.ClientScreen.Width
                    && resized.CenterDeviationPixels <= 1 && resized.TopDeviationPixels == 0,
                "a host-width change keeps primary top-center placement");

            const uint noSize = 0x0001, noZOrder = 0x0004, noActivate = 0x0010, noOwnerZOrder = 0x0200;
            Check(SetWindowPos(handle, IntPtr.Zero, resized.Monitor.Left + 100,
                    resized.WorkArea.Top + 80, 0, 0, noSize | noZOrder | noActivate | noOwnerZOrder),
                "owned HWND can be displaced without activation");
            Pump();
            var restored = WindowPlacement.GetPrimaryDiagnostics(window);
            Check(restored.CenterDeviationPixels <= 1 && restored.TopDeviationPixels == 0,
                "native position changes are corrected back to primary top center");
            Check(!IsWindowVisible(handle), "placement checks never show the test window");
            Check(GetForegroundWindow() == foreground, "placement maintenance does not steal foreground focus");
            return checks;
        }
        finally
        {
            placement?.Dispose(); window.Close();
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
