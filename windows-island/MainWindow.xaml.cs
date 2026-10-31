using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CcfDeadlineIsland.DynamicWinHost;
using CcfDeadlineIsland.Models;
using CcfDeadlineIsland.Services;
using DynamicWin.Main;
using SkiaSharp;
using Forms = System.Windows.Forms;

namespace CcfDeadlineIsland;

public partial class MainWindow : Window
{
    private readonly bool _preview, _demo;
    private readonly string _settingsPath;
    private readonly string? _diagnosticsPath;
    private readonly DeadlineService _service;
    private readonly ConferenceModel _model;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(10) };
    private DeadlineSnapshot? _snapshot;
    private Forms.NotifyIcon? _tray;
    private bool _quitting, _hiddenByUser, _modalOpen, _contextMenuOpen;
    private readonly DockController _dock;
    private bool _dockPending, _dockDragging;
    private System.Windows.Point _dragStartScreen, _lastDragScreen;
    private double _dragGrabTopPixels;
    private Cursor? _preDragCursor;
    private string _error = "", _startupError = "";
    public RendererMain Renderer { get; }
    public void PrepareOffscreenPreview() { _snapshot = DemoSnapshot(); UpdateData(); }

    public MainWindow(bool preview = false, bool demo = false, string? diagnosticsPath = null)
    {
        _preview = preview; _demo = demo;
        _diagnosticsPath = diagnosticsPath;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), preview ? "CCFDeadlineIslandPreview" : "CCFDeadlineIsland");
        _settingsPath = Path.Combine(folder, "settings.json");
        _service = new DeadlineService(Path.Combine(folder, "deadlines.json"));
        _model = new ConferenceModel { Preferences = preview ? new Preferences() : Preferences.Load(_settingsPath),
            LaunchAtStartup = !preview && StartupRegistration.IsEnabled() };
        InitializeComponent();
        _model.Refresh = () => _ = RefreshAsync();
        _model.OpenWebsite = OpenWebsite;
        _model.ExportCalendar = ExportCalendar;
        _model.CopyTime = CopyTime;
        _model.SavePreferences = SavePreferences;
        _model.SetLaunchAtStartup = SetLaunchAtStartup;
        Renderer = new RendererMain(_model);
        _model.TogglePin = () => Renderer.ForceExpanded = !Renderer.ForceExpanded;
        Renderer.ContextMenu = MakeContextMenu();
        Renderer.ContextMenuOpening += (_, _) => { CancelDockGesture(); _contextMenuOpen = true; };
        Renderer.ContextMenuClosing += (_, _) => _contextMenuOpen = false;
        RenderRoot.Children.Add(Renderer);
        _dock = new DockController(this, _model.Preferences, () =>
        {
            var bounds = Renderer.MainIsland.GetRect().Rect;
            return new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }, SavePreferences);
        _dock.Changed += ApplyDockStyle;
        Renderer.GeometryChanged += RendererGeometryChanged;
        ApplyDockStyle();
        if (preview) { ShowInTaskbar = true; Title += " — Preview"; }
        Loaded += OnLoaded; Closing += OnClosing; Closed += OnClosed;
        StateChanged += (_, _) => MaintainTopmost();
        _clock.Tick += (_, _) => Tick();
        _refresh.Tick += async (_, _) => await RefreshAsync();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HitTest);
        if (!_preview) { CreateTray(); WindowPlacement.RegisterShortcut(this, () => { if (Renderer.ForceExpanded) Collapse(); else Reveal(); }); }
        _snapshot = _demo ? DemoSnapshot() : _service.ReadCache();
        UpdateData(); _clock.Start(); _refresh.Start();
        MaintainTopmost();
        if (_preview) Reveal();
        WriteDiagnostics();
        if (!_demo) await RefreshAsync();
    }
    private IntPtr HitTest(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084 || _preview) return IntPtr.Zero;
        int x = unchecked((short)(lParam.ToInt64() & 0xffff)), y = unchecked((short)((lParam.ToInt64() >> 16) & 0xffff));
        var point = PointFromScreen(new System.Windows.Point(x, y));
        if (!Renderer.MainIsland.GetInteractionRect().Contains(SKRect.Create((float)point.X, (float)point.Y, 1, 1))) { handled = true; return new IntPtr(-1); }
        return IntPtr.Zero;
    }
    public void Reveal() { CancelDockGesture(); _hiddenByUser = false; Show(); MaintainTopmost(); Renderer.ForceCollapsed = false; Renderer.ForceExpanded = true; Activate(); Renderer.Focus(); }
    private void Collapse() { CancelDockGesture(); Renderer.Input.CancelAll(); Renderer.ForceExpanded = false; Renderer.ForceCollapsed = true; Renderer.Menu.ShowPage("widgets"); }
    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (TryBeginDockGesture(e)) { e.Handled = true; return; }
        Renderer.ForceCollapsed = false; Renderer.ForceExpanded = true; Activate();
    }

    private bool TryBeginDockGesture(MouseButtonEventArgs e)
    {
        // Button presses retain their existing event/capture path. The empty
        // header/body and the compact surface are the draggable area.
        if (!IsLoaded || !Renderer.IsLoaded || _modalOpen || _contextMenuOpen || _dockPending) return false;
        var local = e.GetPosition(Renderer);
        var point = new DynamicWin.Utils.Vec2(local.X, local.Y);
        if (!Renderer.MainIsland.GetRect().Contains(SKRect.Create((float)local.X, (float)local.Y, .01f, .01f))
            || (Renderer.ContentReady && Renderer.Input.HitTest(point) is not null)) return false;
        var screen = Renderer.PointToScreen(local);
        var island = Renderer.MainIsland.GetRect().Rect;
        var top = Renderer.PointToScreen(new System.Windows.Point(island.Left, island.Top));
        _dragStartScreen = _lastDragScreen = screen;
        _dragGrabTopPixels = screen.Y - top.Y;
        _preDragCursor = Renderer.Cursor;
        _dockPending = true;
        if (!Renderer.CaptureMouse()) { _dockPending = false; return false; }
        return true;
    }

    private void WindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dockPending) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelDockGesture(); return; }
        _lastDragScreen = Renderer.PointToScreen(e.GetPosition(Renderer));
        if (!_dockDragging)
        {
            double dpiScale = VisualTreeHelper.GetDpi(Renderer).DpiScaleX;
            if (Math.Abs(_lastDragScreen.X - _dragStartScreen.X) < SystemParameters.MinimumHorizontalDragDistance * dpiScale
                && Math.Abs(_lastDragScreen.Y - _dragStartScreen.Y) < SystemParameters.MinimumVerticalDragDistance * dpiScale)
            { e.Handled = true; return; }
            _dockDragging = true;
            Renderer.Cursor = Cursors.SizeAll;
            Renderer.BeginDockDrag();
        }
        PreviewDockAt(_lastDragScreen);
        e.Handled = true;
    }

    private void WindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dockPending || e.ChangedButton != MouseButton.Left) return;
        bool dragged = _dockDragging;
        _lastDragScreen = Renderer.PointToScreen(e.GetPosition(Renderer));
        // Clear first: releasing capture raises LostMouseCapture synchronously.
        _dockPending = _dockDragging = false;
        if (dragged)
        {
            _dock.Commit(_lastDragScreen, DesiredDragCenterY(_lastDragScreen));
            Renderer.EndDockDrag(cancelled: false);
            Renderer.Cursor = _preDragCursor;
        }
        if (ReferenceEquals(Mouse.Captured, Renderer)) Renderer.ReleaseMouseCapture();
        if (!dragged)
        { Renderer.ForceCollapsed = false; Renderer.ForceExpanded = true; Activate(); Renderer.Focus(); }
        _dock.Refresh();
        e.Handled = true;
    }

    private void WindowLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dockPending && !ReferenceEquals(Mouse.Captured, Renderer)) CancelDockGesture();
    }

    private double DesiredDragCenterY(System.Windows.Point physicalPointer)
    {
        var island = Renderer.MainIsland.GetRect().Rect;
        var top = Renderer.PointToScreen(new System.Windows.Point(island.Left, island.Top));
        var bottom = Renderer.PointToScreen(new System.Windows.Point(island.Left, island.Bottom));
        return physicalPointer.Y - _dragGrabTopPixels + (bottom.Y - top.Y) / 2;
    }

    private void PreviewDockAt(System.Windows.Point physicalPointer) => _dock.Preview(physicalPointer, DesiredDragCenterY(physicalPointer));

    private void CancelDockGesture()
    {
        if (!_dockPending && !_dockDragging) return;
        _dockPending = _dockDragging = false;
        _dock.Cancel();
        Renderer.EndDockDrag(cancelled: true);
        Renderer.Cursor = _preDragCursor;
        if (ReferenceEquals(Mouse.Captured, Renderer)) Renderer.ReleaseMouseCapture();
    }

    private void ApplyDockStyle()
    {
        // Previewed and committed docks share the same presentation immediately.
        _model.DockSide = _dock.Side;
        DynamicWin.Main.Settings.IslandMode = _dock.Side == "top"
            ? DynamicWin.UI.UIElements.IslandObject.IslandMode.Notch : DynamicWin.UI.UIElements.IslandObject.IslandMode.Island;
    }

    private void RendererGeometryChanged()
    {
        if (!IsLoaded) return;
        if (_dockDragging) PreviewDockAt(_lastDragScreen);
        else _dock.Refresh();
    }
    private void WindowDeactivated(object? sender, EventArgs e)
    {
        CancelDockGesture();
        Renderer.Input.CancelAll();
        if (!_preview && !_modalOpen && !_contextMenuOpen) Collapse();
        MaintainTopmost();
    }
    private void WindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Collapse(); e.Handled = true; return; }
        if (_dockPending) { e.Handled = true; return; }
        if (Renderer.Input.HandleKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
        switch (e.Key) { case Key.F5: _ = RefreshAsync(); break; default: return; }
        e.Handled = true;
    }
    private ContextMenu MakeContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Open conference website", OpenWebsite); Add("Copy deadline times", () => CopyTime()); Add("Export calendar (.ics)", ExportCalendar);
        menu.Items.Add(new Separator()); Add("Refresh", () => _ = RefreshAsync()); Add("Hide", () => { _hiddenByUser = true; Hide(); }); Add("Exit", Quit);
        return menu;
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => _contextMenuOpen = true;
        menu.Closed += (_, _) => _contextMenuOpen = false;
        menu.Items.Add("Show CCF Deadlines", null, (_, _) => Reveal()); menu.Items.Add("Refresh", null, async (_, _) => await RefreshAsync()); menu.Items.Add("Exit", null, (_, _) => Quit());
        var icon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!.Stream;
        _tray = new Forms.NotifyIcon { Text = "CCF Deadline Island · Ctrl+Alt+D", Icon = new System.Drawing.Icon(icon), ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Reveal(); };
    }
    private async Task RefreshAsync()
    {
        if (_model.Loading || _demo) return;
        _model.Loading = true; UpdateStatus();
        try { _snapshot = await _service.FetchAsync(cancellation: _shutdown.Token); _error = ""; if (!_service.SaveCache(_snapshot)) _error = "Cache could not be saved; current data remains available."; }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
        catch (Exception error) { _error = error.Message; }
        finally { _model.Loading = false; if (!_shutdown.IsCancellationRequested) UpdateData(); }
    }
    private void UpdateData()
    {
        _model.Rows = _model.Preferences.Visible(_snapshot?.Conferences ?? [], DateTimeOffset.UtcNow);
        _model.SelectedIndex = Math.Clamp(_model.SelectedIndex, 0, Math.Max(0, _model.Rows.Count - 1));
        DynamicWin.Main.Settings.AllowAnimation = true;
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        string updated = _snapshot is null ? "No saved data" : $"Updated {_snapshot.UpdatedAt.ToLocalTime():HH:mm}";
        _model.Status = _demo ? "Preview · sample conferences" : _model.Loading ? "Refreshing..." : _error.Length > 0 ? $"Offline · {updated}" : updated;
        Renderer.ToolTip = _startupError.Length > 0 ? _startupError : _error.Length > 0 ? _error : null;
    }
    private void Tick()
    {
        UpdateData();
        MaintainTopmost();
        WriteDiagnostics();
    }
    private void MaintainTopmost()
    {
        if (!IsLoaded || _quitting || _hiddenByUser || _modalOpen || _contextMenuOpen) return;
        // Restore shell/show-desktop changes without taking keyboard focus.
        WindowPlacement.EnsureTopmost(this, makeVisible: true);
    }
    private void WriteDiagnostics()
    {
        if (_diagnosticsPath is null) return;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(_diagnosticsPath))!;
            Directory.CreateDirectory(folder);
            var controls = Renderer.Menu.Controls.ToDictionary(pair => pair.Key, pair =>
            {
                var bounds = pair.Value.GetInteractionRect().Rect;
                return new { enabled = pair.Value.ImmediateInputEnabled, left = bounds.Left, top = bounds.Top, width = bounds.Width, height = bounds.Height };
            });
            var placement = WindowPlacement.GetPrimaryDiagnostics(this);
            var island = Renderer.MainIsland.GetRect().Rect;
            double islandCenterPixels = placement.ClientScreen.Left + island.MidX * placement.Dpi / 96.0;
            var snapshot = new
            {
                version = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                page = Renderer.Menu.Page,
                window = new { visible = IsVisible, topmost = Topmost, hiddenByUser = _hiddenByUser, state = WindowState.ToString() },
                primaryPlacement = placement,
                island = new { left = island.Left, top = island.Top, width = island.Width, height = island.Height,
                    centerPixels = islandCenterPixels, centerDeviationPixels = islandCenterPixels - placement.PrimaryCenterX,
                    leftPixels = placement.ClientScreen.Left + island.Left * placement.Dpi / 96.0,
                    rightPixels = placement.ClientScreen.Left + island.Right * placement.Dpi / 96.0,
                    topPixels = placement.ClientScreen.Top + island.Top * placement.Dpi / 96.0,
                    bottomPixels = placement.ClientScreen.Top + island.Bottom * placement.Dpi / 96.0 },
                input = new { downs = Renderer.RoutedPointerDownCount, ups = Renderer.RoutedPointerUpCount,
                    focused = Renderer.Input.FocusedTarget?.InputIdentity, captured = Renderer.Input.CapturedTarget?.InputIdentity,
                    pinned = Renderer.ForceExpanded, dismissed = Renderer.ForceCollapsed },
                ccf = _model.Preferences.Ccf.ToArray(), withinDays = _model.Preferences.WithinDays,
                areas = _model.Preferences.Categories.ToArray(), selectedConference = Renderer.Menu.Selected?.Id,
                submissionOnly = _model.Preferences.SubmissionOnly,
                launchAtStartup = _model.LaunchAtStartup,
                dock = new { side = _dock.Side, position = _dock.Position, dragging = _dockDragging, previewing = _dock.IsPreviewing },
                controls,
                recordedAt = DateTimeOffset.UtcNow
            };
            File.WriteAllText(_diagnosticsPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { Debug.WriteLine($"Preview diagnostics unavailable: {error.Message}"); }
    }
    private ConferenceEdition? Selected => _model.Rows.ElementAtOrDefault(_model.SelectedIndex)?.Conference;
    private void OpenWebsite()
    {
        if (Selected is not { } item || !Uri.TryCreate(item.Website, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { _model.Status = "Could not open website"; Renderer.ToolTip = error.Message; }
    }
    private bool CopyTime()
    {
        if (Selected is not { } item) return false;
        string text = string.Join("\n", item.Deadlines.Where(d => d.Utc > DateTimeOffset.UtcNow)
            .Select(d => $"{item.Heading} | {d.Kind} | {d.LocalTime} | {d.OriginalTime}"));
        if (text.Length == 0) return false;
        try
        {
            System.Windows.Clipboard.SetText(text);
            _model.Status = "Deadline times copied";
            return true;
        }
        catch (ExternalException error)
        {
            _model.Status = "Clipboard unavailable"; Renderer.ToolTip = error.Message;
            return false;
        }
    }
    private void ExportCalendar()
    {
        if (Selected is not { } item) return;
        _modalOpen = true;
        try
        {
            var invalid = Path.GetInvalidFileNameChars(); string name = string.Concat(item.Id.Select(c => invalid.Contains(c) ? '_' : c));
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Calendar (*.ics)|*.ics", FileName = name + ".ics" };
            if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, CalendarExport.Create(item, DateTimeOffset.UtcNow), new UTF8Encoding(false)); _model.Status = "Calendar exported"; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _model.Status = "Calendar export failed"; Renderer.ToolTip = error.Message; }
        finally { _modalOpen = false; }
    }
    private void SavePreferences() { bool saved = _preview || _model.Preferences.Save(_settingsPath); UpdateData(); if (!saved) _model.Status = "Settings could not be saved"; }
    private void SetLaunchAtStartup(bool enabled)
    {
        if (_preview || StartupRegistration.SetEnabled(enabled))
        { _model.LaunchAtStartup = enabled; _startupError = ""; }
        else _startupError = "Could not change launch at startup.";
        Renderer.Menu.Update();
        UpdateStatus();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!_quitting) { e.Cancel = true; _hiddenByUser = true; Collapse(); Hide(); } }
    private void Quit() { _quitting = true; System.Windows.Application.Current.Shutdown(); }
    private void OnClosed(object? sender, EventArgs e) { CancelDockGesture(); _shutdown.Cancel(); _clock.Stop(); _refresh.Stop(); Renderer.GeometryChanged -= RendererGeometryChanged; _dock.Dispose(); WindowPlacement.UnregisterShortcut(this); Renderer.Dispose(); _tray?.Dispose(); _service.Dispose(); }
    public static DeadlineSnapshot DemoSnapshot()
    {
        var now = DateTimeOffset.UtcNow; var rows = new List<ConferenceEdition>();
        foreach (var (title, area, days) in new[] { ("ICML", "AI", 12), ("ACL", "AI", 17), ("ISSTA", "SE", 18), ("SIGMOD", "DB", 23) })
        {
            var time = now.AddDays(days).AddHours(22).AddMinutes(1);
            rows.Add(new ConferenceEdition(title.ToLowerInvariant() + "27", title, 2027, area, "A", "A*", "A", "https://ccfddl.com/", [new("abstract", time.ToString("yyyy-MM-dd HH:mm:ss"), "UTC", time, "Main track"), new("submission", time.AddDays(7).ToString("yyyy-MM-dd HH:mm:ss"), "UTC", time.AddDays(7), "Main track")]));
        }
        return new(now, rows);
    }
}
