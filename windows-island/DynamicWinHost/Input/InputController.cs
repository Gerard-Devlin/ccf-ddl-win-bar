using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.Menu.Menus;
using DynamicWin.Utils;
using SkiaSharp;
using System.Windows.Input;
using WpfKey = System.Windows.Input.Key;

namespace CcfDeadlineIsland.DynamicWinHost.Input;

/// <summary>One input path for native WPF events and deterministic logical-coordinate tests.</summary>
public sealed class InputController : IDisposable
{
    private readonly Func<IEnumerable<UIObject>> _roots;
    private readonly Func<SKRoundRect> _islandMask;
    private UIObject? _captured;
    private UIObject? _focused;
    private UIObject? _keyboardPressed;
    private WpfKey _pressedKey;
    private string _capturedIdentity = "";
    private string _focusedIdentity = "";
    private string _keyboardIdentity = "";
    private bool _keyboardMode;
    private bool _disposed;

    public Vec2? PointerPosition { get; private set; }
    public UIObject? CapturedTarget => _captured;
    public UIObject? FocusedTarget => _focused;
    public bool HasKeyboardFocus => _keyboardMode && _focused is not null;
    public event Action? CaptureCancelled;
    public event Action<string>? Trace;

    public InputController(Func<IEnumerable<UIObject>> roots, Func<SKRoundRect> islandMask)
    {
        _roots = roots;
        _islandMask = islandMask;
        UIObject.InputInvalidated += OnInvalidated;
    }

    public void PointerMove(float x, float y) => PointerMove(new Vec2(x, y));
    public void PointerMove(Vec2 point)
    {
        PointerPosition = new Vec2(point.X, point.Y);
        Validate();
        foreach (var control in AllNodes())
            control.SetEventHover(control.ImmediateInputEnabled && Contains(control.GetInteractionRect(), point));
    }

    public bool PointerDown(float x, float y) => PointerDown(new Vec2(x, y));
    public bool PointerDown(Vec2 point)
    {
        if (_disposed) return false;
        CancelCapture();
        CancelKeyboardPress();
        _keyboardMode = false;
        PointerMove(point);
        var target = HitTest(point);
        if (target is null) { Trace?.Invoke($"down miss {point.X:0.##},{point.Y:0.##}"); return false; }
        _captured = target;
        _capturedIdentity = target.InputIdentity;
        Focus(target, false);
        target.SetEventPressed(true);
        target.OnGlobalMouseDown();
        if (ReferenceEquals(_captured, target) && IsLive(target) && _capturedIdentity == target.InputIdentity)
            target.OnMouseDown();
        Trace?.Invoke($"down {Describe(target)} {point.X:0.##},{point.Y:0.##}");
        Validate();
        return ReferenceEquals(_captured, target);
    }

    public bool PointerUp(float x, float y) => PointerUp(new Vec2(x, y));
    public bool PointerUp(Vec2 point)
    {
        if (_disposed) return false;
        PointerMove(point);
        var target = _captured;
        string identity = _capturedIdentity;
        _captured = null;
        _capturedIdentity = "";
        if (target is null) { Trace?.Invoke($"up uncaptured {point.X:0.##},{point.Y:0.##}"); return false; }
        target.SetEventPressed(false);
        if (!IsLive(target) || identity != target.InputIdentity) return false;
        target.OnGlobalMouseUp();
        bool activate = ReferenceEquals(HitTest(point), target);
        Trace?.Invoke($"up {Describe(target)} activate={activate} {point.X:0.##},{point.Y:0.##}");
        if (activate) target.OnMouseUp();
        return activate;
    }

    public void PointerLeave()
    {
        if (_captured is not null) return;
        PointerPosition = null;
        foreach (var node in AllNodes()) node.SetEventHover(false);
    }

    public UIObject? HitTest(Vec2 point)
    {
        if (!Contains(_islandMask(), point)) return null;
        foreach (var root in _roots().Reverse())
        {
            var found = HitTree(root, point);
            if (found is not null) return found;
        }
        return null;
    }

    private UIObject? HitTree(UIObject node, Vec2 point)
    {
        if (!node.ImmediateInputEnabled) return null;
        for (int index = node.LocalObjects.Count - 1; index >= 0; index--)
        {
            var found = HitTree(node.LocalObjects[index], point);
            if (found is not null) return found;
        }
        return node.IsInputTarget && Contains(InputBounds(node), point) ? node : null;
    }

    private static SKRoundRect InputBounds(UIObject node)
    {
        var rect = node.GetInputRect();
        // Preserve the original checkbox artwork while including its visible text in the target.
        if (node is Checkbox)
        {
            var bounds = rect.Rect;
            foreach (var label in node.LocalObjects.OfType<DWText>())
            {
                using var paint = new SKPaint { Typeface = label.Font, TextSize = label.TextSize };
                var position = label.Position;
                var metrics = paint.FontMetrics;
                bounds.Union(new SKRect(position.X, position.Y + metrics.Ascent,
                    position.X + paint.MeasureText(label.Text), position.Y + metrics.Descent));
            }
            rect = new SKRoundRect(bounds, node.roundRadius);
        }
        return rect;
    }

    public bool HandleKey(WpfKey key, ModifierKeys modifiers = ModifierKeys.None) => KeyDown(key, modifiers);
    public bool Key(WpfKey key, ModifierKeys modifiers = ModifierKeys.None)
    {
        bool handled = KeyDown(key, modifiers);
        if (key is WpfKey.Space or WpfKey.Enter) KeyUp(key);
        return handled;
    }

    public bool KeyDown(WpfKey key, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (_disposed) return false;
        Validate();
        if (key == WpfKey.Tab && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
        {
            CancelKeyboardPress();
            var targets = FocusableNodes();
            if (targets.Count == 0) return false;
            int current = _focused is null ? -1 : targets.IndexOf(_focused);
            int step = modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            int index = current < 0 ? (step > 0 ? 0 : targets.Count - 1) : (current + step + targets.Count) % targets.Count;
            Focus(targets[index], true);
            return true;
        }
        if (key is not (WpfKey.Space or WpfKey.Enter) || _focused is null || !IsLive(_focused)) return false;
        if (_keyboardPressed is not null) return true; // WPF auto-repeat cannot invoke twice.
        CancelCapture();
        _keyboardMode = true;
        var target = _focused;
        _keyboardPressed = target;
        _keyboardIdentity = target.InputIdentity;
        _pressedKey = key;
        target.SetEventPressed(true);
        target.OnGlobalMouseDown();
        if (ReferenceEquals(_keyboardPressed, target) && IsLive(target) && _keyboardIdentity == target.InputIdentity)
            target.OnMouseDown();
        Validate();
        return true;
    }

    public bool KeyUp(WpfKey key)
    {
        if (_keyboardPressed is null || key != _pressedKey) return false;
        var target = _keyboardPressed;
        string identity = _keyboardIdentity;
        _keyboardPressed = null;
        _keyboardIdentity = "";
        target.SetEventPressed(false);
        if (IsLive(target) && ReferenceEquals(_focused, target) && identity == target.InputIdentity)
        {
            target.OnGlobalMouseUp();
            if (IsLive(target) && identity == target.InputIdentity) target.OnMouseUp();
        }
        return true;
    }

    public void CancelCapture()
    {
        bool wasCaptured = _captured is not null;
        _captured?.SetEventPressed(false);
        _captured = null;
        _capturedIdentity = "";
        if (wasCaptured) CaptureCancelled?.Invoke();
    }

    private void CancelKeyboardPress()
    {
        _keyboardPressed?.SetEventPressed(false);
        _keyboardPressed = null;
        _keyboardIdentity = "";
    }
    public void CancelAll()
    {
        CancelCapture();
        CancelKeyboardPress();
        _focused = null;
        _focusedIdentity = "";
        _keyboardMode = false;
    }

    public void Validate()
    {
        if (_captured is not null && (!IsLive(_captured) || _capturedIdentity != _captured.InputIdentity)) CancelCapture();
        if (_keyboardPressed is not null && (!IsLive(_keyboardPressed) || _keyboardIdentity != _keyboardPressed.InputIdentity)) CancelKeyboardPress();
        if (_focused is not null && (!IsLive(_focused) || _focusedIdentity != _focused.InputIdentity))
        {
            _focused = null;
            _focusedIdentity = "";
        }
    }

    private void OnInvalidated(UIObject node) => Validate();
    private bool IsLive(UIObject target) => target.ImmediateInputEnabled && AllNodes().Any(node => ReferenceEquals(node, target));
    private List<UIObject> FocusableNodes() => AllNodes().Where(node => node.IsInputTarget && node.KeyboardFocusable && node.ImmediateInputEnabled).ToList();
    private IEnumerable<UIObject> AllNodes() => _roots().SelectMany(Tree);
    private static IEnumerable<UIObject> Tree(UIObject root)
    {
        yield return root;
        foreach (var node in root.LocalObjects.SelectMany(Tree)) yield return node;
    }
    private void Focus(UIObject target, bool keyboard)
    {
        _focused = target;
        _focusedIdentity = target.InputIdentity;
        _keyboardMode = keyboard;
    }
    private static bool Contains(SKRoundRect rect, Vec2 point) => rect.Contains(SKRect.Create(point.X, point.Y, .01f, .01f));
    private static string Describe(UIObject node) => node.InputIdentity.Length > 0 ? node.InputIdentity : node.GetType().Name;

    public void DrawFocus(SKCanvas canvas)
    {
        Validate();
        if (!_keyboardMode || _focused is null) return;
        var rect = InputBounds(_focused).Rect;
        rect.Inflate(2, 2);
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true, Color = Theme.Primary.Value() };
        canvas.DrawRoundRect(rect, Math.Max(4, _focused.roundRadius + 2), Math.Max(4, _focused.roundRadius + 2), paint);
    }

    public void Dispose()
    {
        CancelAll();
        UIObject.InputInvalidated -= OnInvalidated;
        _disposed = true;
    }
}
