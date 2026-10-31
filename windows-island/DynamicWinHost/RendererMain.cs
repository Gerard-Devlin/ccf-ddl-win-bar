// Rendering/masking flow adapted from DynamicWin Main/RendererMain.cs (main 9f24d17).
// Original IslandObject, font, icon, drawing and spring classes are unchanged.
// UIObject input is adapted in Input/UIObject.cs; the source snapshot is preserved.
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
            if (Instance._updatingIsland && Instance.IsDockDragging)
                return Instance._dragExpanded ? new Vec2(ScreenDimensions.X / 2, 20) : new Vec2(-1000, -1000);
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
    public bool IsDockDragging { get; private set; }
    private bool _dragExpanded;
    private bool _dragWasPinned, _dragWasDismissed;
    private SKRect _lastGeometry;
    private bool _lastExpanded;

    public RendererMain(ConferenceModel model)
    {
        Instance = this;
        Model = model;
        Focusable = true;
        IgnorePixelScaling = true;
        MainIsland = new IslandObject();
        Menu = new ConferenceMenu(MainIsland, model);
        MenuManager.Instance.ActiveMenu = Menu;
        Input = new InputController(() => Menu.UiObjects, () => MainIsland.GetRect());
        Input.CaptureCancelled += ReleaseNativeCapture;
        Menu.PageChanged += _ => Input.CancelAll();
        CompositionTarget.Rendering += OnFrame;
        MouseWheel += (_, e) => { Menu.Scroll(e.Delta); e.Handled = true; };
        PreviewMouseDown += NativePointerDown;
        PreviewMouseUp += NativePointerUp;
        MouseMove += (_, e) => Input.PointerMove(LocalPoint(e));
        MouseLeave += (_, _) => Input.PointerLeave();
        LostMouseCapture += (_, _) => Input.CancelCapture();
        LostKeyboardFocus += (_, _) => Input.CancelAll();
        PreviewKeyDown += (_, e) => { if (Input.HandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (Input.KeyUp(e.Key)) e.Handled = true; };
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

    // Keep the drag surface steady while mouse capture takes the pointer away
    // from the normal hover region. Geometry still uses the upstream spring.
    public void BeginDockDrag()
    {
        if (IsDockDragging) return;
        _dragWasPinned = ForceExpanded;
        _dragWasDismissed = ForceCollapsed;
        // A portrait compact capsule can be taller than a short expanded list.
        // Width distinguishes the two presentations consistently in every dock.
        var compact = Menu.IslandSize();
        var expanded = Menu.IslandSizeBig();
        _dragExpanded = ForceExpanded || Math.Abs(MainIsland.Size.X - expanded.X) < Math.Abs(MainIsland.Size.X - compact.X);
        IsDockDragging = true;
        Input.CancelAll();
    }

    public void EndDockDrag(bool cancelled)
    {
        if (!IsDockDragging) return;
        IsDockDragging = false;
        ForceExpanded = cancelled ? _dragWasPinned : _dragExpanded;
        ForceCollapsed = cancelled ? _dragWasDismissed : !_dragExpanded;
    }

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
        // A shrinking transparent window can raise MouseLeave before its island has
        // finished contracting. Keep dismissal latched until only the compact hit
        // region remains, then let a subsequent pointer entry expand it again.
        var pointer = Input.PointerPosition;
        if (pointer is null)
        {
            var native = Mouse.GetPosition(this);
            pointer = new Vec2(native.X, native.Y);
        }
        if (!IsDockDragging) TryReleaseDismissal(pointer);
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
        DrawSideAttachment(canvas);
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
        canvas.Flush();
    }

    private void DrawSideAttachment(SKCanvas canvas)
    {
        if (!TryGetSideAttachment(out var attachedHalf, out float amount)) return;
        using var paint = new SKPaint
        {
            IsAntialias = Settings.AntiAliasing,
            Color = Theme.IslandBackground.Override(a: amount).Value()
        };
        canvas.DrawRect(attachedHalf, paint);
    }

    public bool ContainsVisibleShell(Vec2 point)
    {
        if (MainIsland.hidden) return false;
        var sample = SKRect.Create(point.X, point.Y, .01f, .01f);
        return MainIsland.GetRect().Contains(sample)
            || (TryGetSideAttachment(out var attachedHalf, out _) && attachedHalf.Contains(sample));
    }

    private bool TryGetSideAttachment(out SKRect attachedHalf, out float amount)
    {
        attachedHalf = SKRect.Empty;
        amount = 0;
        if (MainIsland.hidden || Model.DockSide is not ("left" or "right")) return false;
        var compact = Menu.IslandSize();
        var expanded = Menu.IslandSizeBig();
        float widthRange = expanded.X - compact.X;
        if (widthRange <= 0) return false;
        // Complete the edge-facing half of the rounded compact shell. As the
        // island widens, fade this host-only fill back to the upstream round rect.
        amount = Math.Clamp((expanded.X - MainIsland.Size.X) / widthRange, 0, 1);
        if (amount <= 1f / 255) return false;
        if (amount >= .995f) amount = 1;
        var bounds = MainIsland.GetRect().Rect;
        attachedHalf = Model.DockSide == "left"
            ? new SKRect(bounds.Left, bounds.Top, bounds.MidX, bounds.Bottom)
            : new SKRect(bounds.MidX, bounds.Top, bounds.Right, bounds.Bottom);
        return true;
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
