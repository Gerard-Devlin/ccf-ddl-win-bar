using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CcfDeadlineIsland.Services.Placement;
using Microsoft.Win32;

namespace CcfDeadlineIsland.Services;

/// <summary>Positions the visible island, rather than the transparent host window, at a supported dock.</summary>
public sealed class DockController : IDisposable
{
    private readonly Window _window;
    private readonly Preferences _preferences;
    private readonly Func<Rect> _islandBoundsDip;
    private readonly Action _persist;
    private readonly HwndSourceHook _hook;
    private HwndSource? _source;
    private DispatcherOperation? _pending;
    private string? _previewSide;
    private double _previewPosition;
    private bool _disposed, _applying;

    public string Side => _previewSide ?? DockPlacement.NormalizeSide(_preferences.DockSide);
    public double Position => _previewSide is not null ? _previewPosition : DockPlacement.NormalizePosition(_preferences.DockPosition);
    public bool IsPreviewing => _previewSide is not null;
    public event Action? Changed;

    public DockController(Window window, Preferences preferences, Func<Rect> islandBoundsDip, Action persist)
    {
        _window = window; _preferences = preferences; _islandBoundsDip = islandBoundsDip; _persist = persist;
        _hook = WindowMessage;
        window.SourceInitialized += SourceInitialized;
        window.Loaded += Loaded;
        window.SizeChanged += SizeChanged;
        window.Closed += Closed;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        AttachSource(); Schedule();
    }

    public void Preview(Point physicalPointer) => Preview(physicalPointer, physicalPointer.Y);

    public void Preview(Point physicalPointer, double desiredVisualCenterY)
    {
        if (_disposed || !TryState(out var state)) return;
        double islandWidth = Math.Max(0, _islandBoundsDip().Width * state!.Dpi / 96.0);
        string side = DockPlacement.ChooseSide(state.Monitor, state.WorkArea, physicalPointer.X, physicalPointer.Y, Side, islandWidth);
        double position = DockPlacement.PositionForCenter(state.WorkArea, desiredVisualCenterY);
        bool changed = _previewSide != side || Math.Abs(_previewPosition - position) > .000001;
        _previewSide = side; _previewPosition = position;
        if (changed) Changed?.Invoke();
        Refresh();
    }

    public void Commit(Point physicalPointer) => Commit(physicalPointer, physicalPointer.Y);

    public void Commit(Point physicalPointer, double desiredVisualCenterY)
    {
        Preview(physicalPointer, desiredVisualCenterY);
        if (_previewSide is null) return;
        bool changed = _preferences.DockSide != _previewSide || Math.Abs(_preferences.DockPosition - _previewPosition) > .000001;
        _preferences.DockSide = _previewSide; _preferences.DockPosition = _previewPosition;
        _previewSide = null;
        Changed?.Invoke(); Refresh();
        if (changed) _persist();
    }

    public void Cancel()
    {
        if (_previewSide is null) return;
        _previewSide = null;
        Changed?.Invoke(); Refresh();
    }

    /// <summary>Call after an island animation frame; unchanged physical positions are a no-op.</summary>
    public void Refresh()
    {
        if (_disposed || _applying) return;
        _applying = true;
        try
        {
            // Moving a host that crosses a mixed-DPI monitor boundary can change
            // its DPI/client dimensions synchronously during SetWindowPos. Re-read
            // both native state and animated bounds before correcting that result.
            // This is bounded and only moves the window while its target differs.
            for (int pass = 0; pass < 4; pass++)
            {
                if (!TryState(out var state)) return;
                var island = _islandBoundsDip();
                if (island.IsEmpty || island.Width <= 0 || island.Height <= 0) return;
                double scale = state!.Dpi / 96.0;
                var pixels = new PixelRect(Round(island.Left * scale), Round(island.Top * scale),
                    Round(island.Right * scale), Round(island.Bottom * scale));
                if (pixels.Width <= 0 || pixels.Height <= 0) return;
                var position = DockPlacement.Calculate(state.Monitor, state.WorkArea, state.Window,
                    state.ClientScreen, pixels, Side, Position);
                if (position.X == state.Window.Left && position.Y == state.Window.Top) return;
                if (!WindowPlacement.ApplyPhysicalPosition(_window, position)) return;
            }
        }
        finally { _applying = false; }
    }

    private static int Round(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private bool TryState(out PlacementDiagnostics? state)
    {
        state = null;
        if (new WindowInteropHelper(_window).Handle == IntPtr.Zero) return false;
        try { state = WindowPlacement.GetPrimaryDiagnostics(_window); return true; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { return false; }
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
        _source = HwndSource.FromHwnd(handle); _source?.AddHook(_hook);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0018 || (message == 0x0047 && !_applying)) Schedule();
        return IntPtr.Zero;
    }

    private void Schedule()
    {
        var dispatcher = _window.Dispatcher;
        if (_disposed || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Schedule)); return; }
        if (_pending?.Status == DispatcherOperationStatus.Pending) return;
        _pending = dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pending = null; if (_disposed) return;
            AttachSource(); Refresh();
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _window.SourceInitialized -= SourceInitialized; _window.Loaded -= Loaded;
        _window.SizeChanged -= SizeChanged; _window.Closed -= Closed;
        _pending?.Abort(); _pending = null;
        try { _source?.RemoveHook(_hook); } catch (ObjectDisposedException) { }
        _source = null;
    }
}
