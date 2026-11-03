// Rendering/masking flow adapted from DynamicWin Main/RendererMain.cs (main 9f24d17).
// Original IslandObject, font, icon, drawing and spring classes are unchanged.
// UIObject input is adapted in Input/UIObject.cs; the source snapshot is preserved.
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CcfDeadlineIsland.DynamicWinHost;
using CcfDeadlineIsland.DynamicWinHost.Input;
using DynamicWin.UI;
using DynamicWin.UI.Menu;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using DynamicWin.WPFBinders;
using SkiaSharp;

namespace DynamicWin.Main;

public sealed class RendererMain : SKElement, IDisposable
{
    public static RendererMain Instance { get; private set; } = null!;
    public static Vec2 ScreenDimensions => new(Instance.ActualWidth, Instance.ActualHeight);
    public static Vec2 CursorPosition
    {
        get
        {
            if (Instance._updatingIsland && Instance.ForceExpanded) return new Vec2(ScreenDimensions.X / 2, 20);
            if (Instance._updatingIsland && Instance.ForceCollapsed) return new Vec2(-1000, -1000);
            if (Instance.Input?.PointerPosition is { } point) return new Vec2(point.X, point.Y);
            var position = Mouse.GetPosition(Instance);
            return new Vec2(position.X, position.Y);
        }
    }
    public IslandObject MainIsland { get; }
    public ConferenceMenu Menu { get; }
    public ConferenceModel Model { get; }
    public InputController Input { get; }
    public bool ContentReady
    {
        get
        {
            var target = Menu.IslandSizeBig();
            return MainIsland.IsHovering && Math.Abs(MainIsland.Size.X - target.X) < .5f
                && Math.Abs(MainIsland.Size.Y - target.Y) < .5f;
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new IslandAutomationPeer(this);
    public float alphaOverride = 1f;
    public float blurOverride;
    public float DeltaTime { get; private set; } = 1f / 60;
    private bool _forceExpanded;
    private bool _forceCollapsed;
    public bool ForceExpanded
    {
        get => _forceExpanded;
        set { if (_forceExpanded == value) return; _forceExpanded = value; if (!value) Input?.CancelAll(); }
    }
    public bool ForceCollapsed
    {
        get => _forceCollapsed;
        set { if (_forceCollapsed == value) return; _forceCollapsed = value; if (value) Input?.CancelAll(); }
    }
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _disposed;
    private bool _updatingIsland;
    private Vec2? _injectedPointer;
    private bool _lastRoutedActivation;
    public int RoutedPointerDownCount { get; private set; }
    public int RoutedPointerUpCount { get; private set; }
    public Action? ExpansionChanged;
    public event Action? GeometryChanged;
    private SKRect _lastGeometry;
    private bool _lastExpanded;

    public RendererMain(ConferenceModel model)
    {
        Instance = this;
        Model = model;
        Focusable = true;
        IgnorePixelScaling = true;
        MainIsland = new IslandObject { roundRadius = IslandDesign.ShellRadius };
        Menu = new ConferenceMenu(MainIsland, model);
        MenuManager.Instance.ActiveMenu = Menu;
        Input = new InputController(() => Menu.UiObjects, () => MainIsland.GetRect());
        Input.CaptureCancelled += ReleaseNativeCapture;
        Menu.PageChanged += _ => Input.CancelAll();
        CompositionTarget.Rendering += OnFrame;
        MouseWheel += (_, e) =>
        {
            // Enlarged content uses the hidden-bar viewport; ordinary lists use row paging.
            if (Parent is ScrollViewer viewport && viewport.ScrollableHeight > .5)
            {
                bool canScroll = e.Delta > 0 ? viewport.VerticalOffset > .5
                    : viewport.VerticalOffset < viewport.ScrollableHeight - .5;
                if (canScroll) return;
            }
            if (Menu.Page != "settings") { Menu.Scroll(e.Delta); e.Handled = true; }
        };
        PreviewMouseDown += NativePointerDown;
        PreviewMouseUp += NativePointerUp;
        MouseMove += (_, e) => Input.PointerMove(LocalPoint(e));
        MouseLeave += (_, _) => Input.PointerLeave();
        LostMouseCapture += (_, _) => Input.CancelCapture();
        LostKeyboardFocus += (_, _) => Input.CancelAll();
        PreviewKeyDown += (_, e) => { if (HandleKeyDown(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (HandleKeyUp(e.Key)) e.Handled = true; };
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        // Keyboard and pointer activation obey the same layout-readiness rule.
        if (key is Key.Space or Key.Enter && !ContentReady)
        {
            Input.CancelAll();
            return true;
        }
        if (ContentReady && Menu.Page == "list" && modifiers == ModifierKeys.None
            && key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            int steps = key is Key.PageUp or Key.PageDown ? 5 : 1;
            int delta = key is Key.Up or Key.PageUp ? 120 : -120;
            for (int i = 0; i < steps; i++) Menu.Scroll(delta);
            Input.Validate();
            InvalidateVisual();
            return true;
        }
        return Input.HandleKey(key, modifiers);
    }

    public bool HandleKeyUp(Key key)
    {
        if (key is Key.Space or Key.Enter && !ContentReady)
        {
            Input.CancelAll();
            return true;
        }
        return Input.KeyUp(key);
    }

    private Vec2 LocalPoint(MouseEventArgs args)
    {
        if (_injectedPointer is { } point) return new Vec2(point.X, point.Y);
        var native = args.GetPosition(this);
        return new Vec2(native.X, native.Y);
    }

    private void NativePointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        RoutedPointerDownCount++;
        ForceCollapsed = false;
        // A click opening the compact island must not hit a navigation control
        // that is moving under the pointer during the expansion spring.
        bool accepted = ContentReady && Input.PointerDown(LocalPoint(e));
        if (accepted && _injectedPointer is null && IsLoaded)
        {
            Focus();
            CaptureMouse();
        }
        // Keep the event bubbling so the window's existing pin/activation handler runs.
        InvalidateVisual();
    }

    private void NativePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        RoutedPointerUpCount++;
        _lastRoutedActivation = Input.PointerUp(LocalPoint(e));
        ReleaseNativeCapture();
        InvalidateVisual();
    }

    private void ReleaseNativeCapture()
    {
        if (ReferenceEquals(Mouse.Captured, this)) ReleaseMouseCapture();
    }

    public void CancelInput() => Input.CancelAll();

    // The test position is scoped to this routed event; native events always use GetPosition.
    public bool RoutePointerDown(float x, float y)
    {
        _injectedPointer = new Vec2(x, y);
        try
        {
            RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = this });
            return Input.CapturedTarget is not null;
        }
        finally { _injectedPointer = null; }
    }
    public bool RoutePointerUp(float x, float y)
    {
        _injectedPointer = new Vec2(x, y);
        _lastRoutedActivation = false;
        try
        {
            RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseUpEvent, Source = this });
            return _lastRoutedActivation;
        }
        finally { _injectedPointer = null; }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_disposed || !IsVisible) return;
        var elapsed = _clock.Elapsed;
        if ((elapsed - _lastFrame).TotalMilliseconds < 12) return;
        DeltaTime = (float)Math.Clamp((elapsed - _lastFrame).TotalSeconds, 1.0 / 120, 1.0 / 20);
        _lastFrame = elapsed;
        UpdateIsland();
        Menu.Update();
        foreach (var element in Menu.UiObjects.ToArray()) element.UpdateCall(DeltaTime);
        Input.Validate();
        UpdateHintsAndAutomation();
        // A shrinking transparent window can raise MouseLeave before its island has
        // finished contracting. Keep dismissal latched until only the compact hit
        // region remains, then let a subsequent pointer entry expand it again.
        var pointer = Input.PointerPosition;
        if (pointer is null)
        {
            var native = Mouse.GetPosition(this);
            pointer = new Vec2(native.X, native.Y);
        }
        TryReleaseDismissal(pointer);
        if (_lastExpanded != MainIsland.IsHovering)
        {
            _lastExpanded = MainIsland.IsHovering;
            ExpansionChanged?.Invoke();
        }
        NotifyGeometryChanged();
        InvalidateVisual();
    }

    private void NotifyGeometryChanged()
    {
        var current = MainIsland.GetRect().Rect;
        if (Math.Abs(current.Left - _lastGeometry.Left) < .01f && Math.Abs(current.Top - _lastGeometry.Top) < .01f
            && Math.Abs(current.Right - _lastGeometry.Right) < .01f && Math.Abs(current.Bottom - _lastGeometry.Bottom) < .01f) return;
        _lastGeometry = current;
        GeometryChanged?.Invoke();
    }

    public bool TryReleaseDismissal(Vec2 pointer)
    {
        if (!ForceCollapsed || ForceExpanded || MainIsland.IsHovering || MainIsland.hidden) return false;
        var compact = Menu.IslandSize();
        if (Math.Abs(MainIsland.Size.X - compact.X) >= .5f ||
            Math.Abs(MainIsland.Size.Y - compact.Y) >= .5f) return false;
        if (MainIsland.GetInteractionRect().Contains(SKRect.Create(pointer.X, pointer.Y, .01f, .01f))) return false;
        ForceCollapsed = false;
        return true;
    }

    private void UpdateIsland()
    {
        _updatingIsland = true;
        try { MainIsland.UpdateCall(DeltaTime); }
        finally { _updatingIsland = false; }
    }

    private WriteableBitmap? _surfaceBitmap;
    protected override void OnRender(DrawingContext drawingContext)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        // Rasterize at display DPI * accessibility scale, so WPF never magnifies a low-resolution bitmap.
        var dpi = source.CompositionTarget.TransformToDevice;
        double sx = dpi.M11 * LayoutTransform.Value.M11, sy = dpi.M22 * LayoutTransform.Value.M22;
        int width = (int)Math.Ceiling(ActualWidth * sx), height = (int)Math.Ceiling(ActualHeight * sy);
        if (width <= 0 || height <= 0) return;
        if (_surfaceBitmap is null || _surfaceBitmap.PixelWidth != width || _surfaceBitmap.PixelHeight != height)
            _surfaceBitmap = new WriteableBitmap(width, height, 96 * sx, 96 * sy, PixelFormats.Pbgra32, null);
        _surfaceBitmap.Lock();
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                _surfaceBitmap.BackBuffer, _surfaceBitmap.BackBufferStride);
            surface.Canvas.Scale((float)sx, (float)sy);
            DrawScene(surface.Canvas);
            _surfaceBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally { _surfaceBitmap.Unlock(); }
        drawingContext.DrawImage(_surfaceBitmap, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        DrawScene(e.Surface.Canvas);
    }

    public void DrawScene(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Transparent);
        if (Menu is null) return;
        MainIsland.DrawCall(canvas);
        if (MainIsland.hidden) return;
        foreach (var element in Menu.UiObjects)
        {
            int saved = canvas.Save();
            if (element.maskInToIsland)
            {
                var mask = MainIsland.GetRect();
                mask.Deflate(new SKSize(1, 1));
                canvas.ClipRoundRect(mask, antialias: true);
            }
            element.DrawCall(canvas);
            canvas.RestoreToCount(saved);
        }
        int focusSaved = canvas.Save();
        canvas.ClipRoundRect(MainIsland.GetRect(), antialias: true);
        Input.DrawFocus(canvas);
        canvas.RestoreToCount(focusSaved);
        Menu.DrawOverlays(canvas);
        canvas.Flush();
    }

    private UIObject? _lastHintTarget;
    private string? _lastHint;
    private void UpdateHintsAndAutomation()
    {
        var target = Input.HasKeyboardFocus ? Input.FocusedTarget
            : Input.PointerPosition is { } point ? Input.HitTest(point) : null;
        string? hint = MainIsland.IsHovering ? Menu.Hint(target) : Menu.AccessibleSummary;
        if (!ReferenceEquals(target, _lastHintTarget) || hint != _lastHint)
        {
            _lastHintTarget = target; _lastHint = hint;
            if (ToolTip is ToolTip previous) previous.IsOpen = false;
            ToolTip = hint is null ? null : new ToolTip { Content = hint, MaxWidth = 460 };
            if (Input.HasKeyboardFocus && target is not null)
            {
                var r = target.GetInputRect().Rect;
                BringIntoView(new Rect(r.Left, r.Top, r.Width, r.Height));
                if (ToolTip is ToolTip tip && target is ConferenceMenu.MenuButton { HelpText.Length: > 0 })
                { tip.PlacementTarget = this; tip.IsOpen = true; }
            }
        }
        if (UIElementAutomationPeer.FromElement(this) is IslandAutomationPeer peer) peer.Synchronize();
    }

    // Offscreen verification uses these same production objects, not a second UI implementation.
    public void Settle(bool expanded, int frames = 180)
    {
        ForceExpanded = expanded;
        ForceCollapsed = !expanded;
        for (int i = 0; i < frames; i++)
        {
            DeltaTime = 1f / 60;
            UpdateIsland();
            Menu.Update();
            foreach (var element in Menu.UiObjects.ToArray()) element.UpdateCall(DeltaTime);
            Input.Validate();
            NotifyGeometryChanged();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CompositionTarget.Rendering -= OnFrame;
        Input.Dispose();
        ReleaseNativeCapture();
        foreach (var element in Menu.UiObjects) element.DestroyCall();
        MainIsland.DestroyCall();
    }
}
