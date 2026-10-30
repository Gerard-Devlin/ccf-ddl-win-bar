// Rendering/masking flow adapted from DynamicWin Main/RendererMain.cs (main 9f24d17).
// The source UIObject, IslandObject, font, icon, drawing and spring classes are unchanged.
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CcfDeadlineIsland.DynamicWinHost;
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
            var position = Mouse.GetPosition(Instance);
            return new Vec2(position.X, position.Y);
        }
    }
    public IslandObject MainIsland { get; }
    public ConferenceMenu Menu { get; }
    public ConferenceModel Model { get; }
    public float alphaOverride = 1f;
    public float blurOverride;
    public float DeltaTime { get; private set; } = 1f / 60;
    public bool ForceExpanded { get; set; }
    public bool ForceCollapsed { get; set; }
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _disposed;
    private bool _updatingIsland;
    public Action? ExpansionChanged;
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
        CompositionTarget.Rendering += OnFrame;
        MouseWheel += (_, e) => { Menu.Scroll(e.Delta); e.Handled = true; };
        MouseLeftButtonDown += (_, _) => { ForceCollapsed = false; };
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
        if (_lastExpanded != MainIsland.IsHovering)
        {
            _lastExpanded = MainIsland.IsHovering;
            ExpansionChanged?.Invoke();
        }
        InvalidateVisual();
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
        canvas.Flush();
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
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CompositionTarget.Rendering -= OnFrame;
        foreach (var element in Menu.UiObjects) element.DestroyCall();
        MainIsland.DestroyCall();
    }
}
