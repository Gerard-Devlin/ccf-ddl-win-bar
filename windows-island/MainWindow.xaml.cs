using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly DeadlineService _service;
    private readonly ConferenceModel _model;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(10) };
    private DeadlineSnapshot? _snapshot;
    private Forms.NotifyIcon? _tray;
    private bool _quitting, _hiddenByUser, _modalOpen, _hiddenByFullscreen;
    private string _error = "";
    public RendererMain Renderer { get; }
    public void PrepareOffscreenPreview() { _snapshot = DemoSnapshot(); UpdateData(); }

    public MainWindow(bool preview = false, bool demo = false)
    {
        _preview = preview; _demo = demo;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), preview ? "CCFDeadlineIslandPreview" : "CCFDeadlineIsland");
        _settingsPath = Path.Combine(folder, "settings.json");
        _service = new DeadlineService(Path.Combine(folder, "deadlines.json"));
        _model = new ConferenceModel { Preferences = preview ? new Preferences() : Preferences.Load(_settingsPath) };
        InitializeComponent();
        _model.Refresh = () => _ = RefreshAsync();
        _model.OpenWebsite = OpenWebsite;
        _model.ExportCalendar = ExportCalendar;
        _model.CopyTime = CopyTime;
        _model.SavePreferences = SavePreferences;
        Renderer = new RendererMain(_model);
        _model.TogglePin = () => Renderer.ForceExpanded = !Renderer.ForceExpanded;
        Renderer.ContextMenu = MakeContextMenu();
        Renderer.MouseLeftButtonDown += (_, _) => { Renderer.ForceCollapsed = false; Renderer.ForceExpanded = true; Activate(); };
        Renderer.MouseLeave += (_, _) => Renderer.ForceCollapsed = false;
        RenderRoot.Children.Add(Renderer);
        if (preview) { ShowInTaskbar = true; Topmost = false; Title += " — Preview"; }
        Loaded += OnLoaded; Closing += OnClosing; Closed += OnClosed;
        _clock.Tick += (_, _) => Tick();
        _refresh.Tick += async (_, _) => await RefreshAsync();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HitTest);
        var screens = Forms.Screen.AllScreens;
        var screen = screens[Math.Clamp(_model.Preferences.Monitor, 0, screens.Length - 1)];
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var origin = transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var size = transform.Transform(new System.Windows.Point(screen.WorkingArea.Width, screen.WorkingArea.Height));
        Left = origin.X + (size.X - Width) / 2; Top = origin.Y;
        if (!_preview) { CreateTray(); WindowPlacement.RegisterShortcut(this, () => { if (Renderer.ForceExpanded) Collapse(); else Reveal(); }); }
        _snapshot = _demo ? DemoSnapshot() : _service.ReadCache();
        UpdateData(); _clock.Start(); _refresh.Start();
        if (_preview) Reveal();
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
    public void Reveal() { _hiddenByUser = false; _hiddenByFullscreen = false; Show(); Renderer.ForceCollapsed = false; Renderer.ForceExpanded = true; Activate(); Renderer.Focus(); }
    private void Collapse() { Renderer.ForceExpanded = false; Renderer.ForceCollapsed = true; Renderer.Menu.ShowPage("widgets"); }
    private void WindowDeactivated(object? sender, EventArgs e) { if (!_preview && !_modalOpen) Collapse(); }
    private void WindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key) { case Key.Escape: Collapse(); break; case Key.Left: Renderer.Menu.SelectRelative(-1); break; case Key.Right: Renderer.Menu.SelectRelative(1); break; case Key.Enter: OpenWebsite(); break; case Key.F5: _ = RefreshAsync(); break; default: return; }
        e.Handled = true;
    }
    private ContextMenu MakeContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Open conference website", OpenWebsite); Add("Copy deadline times", CopyTime); Add("Export calendar (.ics)", ExportCalendar);
        menu.Items.Add(new Separator()); Add("Refresh", () => _ = RefreshAsync()); Add("Hide", () => { _hiddenByUser = true; Hide(); }); Add("Exit", Quit);
        return menu;
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
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
        DynamicWin.Main.Settings.AllowAnimation = !_model.Preferences.ReducedMotion && SystemParameters.ClientAreaAnimation;
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        string updated = _snapshot is null ? "No saved data" : $"Updated {_snapshot.UpdatedAt.ToLocalTime():HH:mm}";
        _model.Status = _demo ? "Preview · sample conferences" : _model.Loading ? "Refreshing..." : _error.Length > 0 ? $"Offline · {updated}" : updated;
        Renderer.ToolTip = _error.Length > 0 ? _error : null;
    }
    private void Tick()
    {
        UpdateData();
        if (_preview || _hiddenByUser) return;
        bool fullscreen = WindowPlacement.ForegroundIsFullscreen(this);
        if (fullscreen && !_hiddenByFullscreen) { _hiddenByFullscreen = true; Collapse(); Hide(); }
        else if (!fullscreen && _hiddenByFullscreen) { _hiddenByFullscreen = false; Show(); }
    }
    private ConferenceEdition? Selected => _model.Rows.ElementAtOrDefault(_model.SelectedIndex)?.Conference;
    private void OpenWebsite()
    {
        if (Selected is not { } item || !Uri.TryCreate(item.Website, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { _model.Status = "Could not open website"; Renderer.ToolTip = error.Message; }
    }
    private void CopyTime()
    {
        if (Selected is not { } item) return;
        try { System.Windows.Clipboard.SetText(string.Join("\n", item.Deadlines.Where(d => d.Utc > DateTimeOffset.UtcNow).Select(d => $"{item.Heading} | {d.Kind} | {d.LocalTime} | {d.OriginalTime}"))); _model.Status = "Deadline times copied"; }
        catch (ExternalException error) { _model.Status = "Clipboard unavailable"; Renderer.ToolTip = error.Message; }
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
    private void OnClosing(object? sender, CancelEventArgs e) { if (!_quitting) { e.Cancel = true; _hiddenByUser = true; Collapse(); Hide(); } }
    private void Quit() { _quitting = true; System.Windows.Application.Current.Shutdown(); }
    private void OnClosed(object? sender, EventArgs e) { _shutdown.Cancel(); _clock.Stop(); _refresh.Stop(); WindowPlacement.UnregisterShortcut(this); Renderer.Dispose(); _tray?.Dispose(); _service.Dispose(); }
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
