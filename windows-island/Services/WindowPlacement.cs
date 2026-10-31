using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CcfDeadlineIsland.Services.Placement;
using Microsoft.Win32;

namespace CcfDeadlineIsland.Services;

public static class WindowPlacement
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xCCF;
    private const uint MonitorPrimary = 1;
    private static readonly IntPtr PerMonitorV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect Pixels => new(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref NativeRect rectangle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

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

    public static void UnregisterShortcut(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, HotkeyId);
    }

    /// <summary>Restore the native topmost band without activating or moving the window.</summary>
    public static bool EnsureTopmost(Window window, bool makeVisible = false)
    {
        window.Topmost = true;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (makeVisible && IsIconic(handle)) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
        const uint noSize = 0x0001, noMove = 0x0002, noActivate = 0x0010, noOwnerZOrder = 0x0200, showWindow = 0x0040;
        return SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
            noSize | noMove | noActivate | noOwnerZOrder | (makeVisible ? showWindow : 0));
    }

    /// <summary>Attach once; dispose when the owning window closes.</summary>
    public static IDisposable AttachPrimary(Window window) => new PrimaryAttachment(window);

    [Obsolete("Use AttachPrimary; placement is fixed to the primary display in physical pixels.")]
    public static void CenterAtTop(Window window, int monitorIndex, int topOffset) => RecenterPrimary(new WindowInteropHelper(window).Handle);

    public static PlacementDiagnostics GetPrimaryDiagnostics(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Placement diagnostics require an initialized HWND.");
        if (!TryReadState(handle, out var state)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read physical window/primary-monitor bounds.");
        return state!;
    }

    private static bool TryPrimaryMonitor(out MonitorInfo information)
    {
        MonitorInfo selected = default;
        bool Visit(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data)
        {
            var candidate = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref candidate) && (candidate.Flags & MonitorPrimary) != 0)
            {
                selected = candidate;
                return false;
            }
            return true;
        }
        // A false return also occurs when our callback intentionally stops at the primary.
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Visit, IntPtr.Zero);
        information = selected;
        return selected.Monitor.Right > selected.Monitor.Left && selected.Monitor.Bottom > selected.Monitor.Top;
    }

    private static bool TryReadState(IntPtr handle, out PlacementDiagnostics? state)
    {
        state = null;
        var previousContext = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            if (!TryPrimaryMonitor(out var primary) || !GetWindowRect(handle, out var window) || !GetClientRect(handle, out var client)) return false;
            var origin = new NativePoint { X = client.Left, Y = client.Top };
            var end = new NativePoint { X = client.Right, Y = client.Bottom };
            if (!ClientToScreen(handle, ref origin) || !ClientToScreen(handle, ref end)) return false;
            var clientScreen = new PixelRect(origin.X, origin.Y, end.X, end.Y);
            if (clientScreen.Width <= 0 || clientScreen.Height <= 0 || window.Pixels.Width <= 0 || window.Pixels.Height <= 0) return false;
            var expected = PrimaryPlacement.Calculate(primary.Monitor.Pixels, primary.WorkArea.Pixels, window.Pixels, clientScreen);
            state = new PlacementDiagnostics(primary.Monitor.Pixels, primary.WorkArea.Pixels, window.Pixels, clientScreen, GetDpiForWindow(handle), expected);
            return true;
        }
        finally { if (previousContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousContext); }
    }

    private static void RecenterPrimary(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !TryReadState(handle, out var state)) return;
        var position = state!.ExpectedWindowPosition;
        if (state.Window.Left == position.X && state.Window.Top == position.Y) return;
        var previousContext = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            const uint noSize = 0x0001, noZOrder = 0x0004, noActivate = 0x0010, noOwnerZOrder = 0x0200;
            SetWindowPos(handle, IntPtr.Zero, position.X, position.Y, 0, 0, noSize | noZOrder | noActivate | noOwnerZOrder);
        }
        finally { if (previousContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousContext); }
    }

    private sealed class PrimaryAttachment : IDisposable
    {
        private readonly Window _window;
        private readonly HwndSourceHook _hook;
        private HwndSource? _source;
        private DispatcherOperation? _pending;
        private bool _disposed;
        private bool _applying;

        public PrimaryAttachment(Window window)
        {
            _window = window;
            _hook = WindowMessage;
            window.SourceInitialized += SourceInitialized;
            window.Loaded += Loaded;
            window.SizeChanged += SizeChanged;
            window.Closed += Closed;
            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
            AttachSource();
            Schedule();
        }

        private void SourceInitialized(object? sender, EventArgs e) { AttachSource(); Schedule(); }
        private void Loaded(object sender, RoutedEventArgs e) => Schedule();
        private void SizeChanged(object sender, SizeChangedEventArgs e) => Schedule();
        private void DisplaySettingsChanged(object? sender, EventArgs e) => Schedule();
        private void Closed(object? sender, EventArgs e) => Dispose();

        private void AttachSource()
        {
            if (_disposed || _source is not null) return;
            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(_hook);
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // WM_DPICHANGED is left unhandled so WPF applies the suggested DPI
            // dimensions first; the queued correction uses its final native width.
            if (message is 0x02E0 or 0x007E or 0x001A or 0x0018 || (message == 0x0047 && !_applying)) Schedule();
            return IntPtr.Zero;
        }

        private void Schedule()
        {
            var dispatcher = _window.Dispatcher;
            if (_disposed || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Schedule));
                return;
            }
            if (_pending?.Status == DispatcherOperationStatus.Pending) return;
            _pending = dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _pending = null;
                if (_disposed) return;
                AttachSource();
                _applying = true;
                try { RecenterPrimary(new WindowInteropHelper(_window).Handle); }
                finally { _applying = false; }
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            _window.SourceInitialized -= SourceInitialized;
            _window.Loaded -= Loaded;
            _window.SizeChanged -= SizeChanged;
            _window.Closed -= Closed;
            _pending?.Abort();
            _pending = null;
            try { _source?.RemoveHook(_hook); } catch (ObjectDisposedException) { }
            _source = null;
        }
    }
}
