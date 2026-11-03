using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using DynamicWin.Main;
using DynamicWin.UI;

namespace CcfDeadlineIsland.DynamicWinHost;

// Native UI Automation children for the custom Skia controls. No ticking live region:
// names are queried on focus, while state changes and deliberate feedback are announced.
internal sealed class IslandAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    private readonly RendererMain _renderer;
    private readonly Dictionary<UIObject, ControlPeer> _peers = new();
    private string _structure = "", _feedback = "";
    private UIObject? _focused;
    public IslandAutomationPeer(RendererMain renderer) : base(renderer) => _renderer = renderer;
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);
    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => true;
    public IRawElementProviderSimple[] GetSelection() => _renderer.Menu.Controls.Values
        .OfType<ConferenceMenu.ToggleButton>().Where(c => c.ImmediateInputEnabled && c.SingleChoice && c.IsSelected)
        .Select(c => ProviderFromPeer(Get(c))).ToArray();
    protected override string GetClassNameCore() => "CCFDeadlineIsland";
    protected override string GetNameCore() => _renderer.Menu.AccessibleSummary;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    protected override List<AutomationPeer> GetChildrenCore() => _renderer.Menu.Controls.Values
        .Where(c => c.ImmediateInputEnabled).Select(c => (AutomationPeer)Get(c)).ToList();
    private ControlPeer Get(UIObject control)
    {
        if (!_peers.TryGetValue(control, out var peer)) _peers[control] = peer = new ControlPeer(_renderer, control, this);
        return peer;
    }
    public void Synchronize()
    {
        string structure = string.Join("|", _renderer.Menu.Controls.Values.Where(c => c.ImmediateInputEnabled).Select(c => c.InputIdentity));
        if (structure != _structure) { _structure = structure; ResetChildrenCache(); InvalidatePeer(); RaiseAutomationEvent(AutomationEvents.StructureChanged); }
        foreach (var peer in _peers.Values) peer.Synchronize();
        var focused = _renderer.Input.FocusedTarget;
        if (focused is not null && !ReferenceEquals(focused, _focused))
            Get(focused).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        _focused = focused;
        string feedback = _renderer.Menu.Feedback;
        if (feedback.Length > 0 && feedback != _feedback)
            RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, feedback, "operation");
        _feedback = feedback;
    }
    private sealed class ControlPeer : AutomationPeer, IInvokeProvider, IToggleProvider, ISelectionItemProvider
    {
        private readonly RendererMain _renderer;
        private readonly UIObject _control;
        private readonly IslandAutomationPeer _parent;
        private bool _selected;
        private ConferenceMenu.MenuButton? Button => _control as ConferenceMenu.MenuButton;
        private ConferenceMenu.ToggleButton? ToggleButton => _control as ConferenceMenu.ToggleButton;
        public ControlPeer(RendererMain renderer, UIObject control, IslandAutomationPeer parent)
        { _renderer = renderer; _control = control; _parent = parent; }
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
        {
            PatternInterface.Invoke when ToggleButton is null => this,
            PatternInterface.Toggle when ToggleButton is { SingleChoice: false } => this,
            PatternInterface.SelectionItem when ToggleButton is { SingleChoice: true } => this,
            _ => null
        };
        protected override string GetNameCore() => Button is ConferenceMenu.ConferenceRow
            ? _renderer.Menu.Hint(_control) ?? Button.ActionName : Button?.ActionName ?? "";
        protected override string GetHelpTextCore() => Button?.HelpText ?? "";
        protected override string GetAutomationIdCore() => _control.InputIdentity;
        protected override string GetClassNameCore() => "IslandControl";
        protected override AutomationControlType GetAutomationControlTypeCore() => ToggleButton is { SingleChoice: true }
            ? AutomationControlType.RadioButton : ToggleButton is not null ? AutomationControlType.CheckBox : AutomationControlType.Button;
        protected override Rect GetBoundingRectangleCore()
        {
            if (!_renderer.IsVisible || PresentationSource.FromVisual(_renderer) is null) return Rect.Empty;
            var r = _control.GetInputRect().Rect;
            var first = _renderer.PointToScreen(new Point(r.Left, r.Top));
            var last = _renderer.PointToScreen(new Point(r.Right, r.Bottom));
            var bounds = new Rect(first, last);
            if (_renderer.Parent is System.Windows.Controls.ScrollViewer viewport)
            {
                var viewportBounds = new Rect(viewport.PointToScreen(new Point()),
                    viewport.PointToScreen(new Point(viewport.ActualWidth, viewport.ActualHeight)));
                bounds.Intersect(viewportBounds);
            }
            return bounds;
        }
        protected override Point GetClickablePointCore() { var r = GetBoundingRectangleCore(); return r.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); }
        protected override bool IsOffscreenCore() => !_control.ImmediateInputEnabled || !_renderer.MainIsland.IsHovering || GetBoundingRectangleCore().IsEmpty;
        protected override bool IsEnabledCore() => _control.ImmediateInputEnabled;
        protected override bool IsKeyboardFocusableCore() => _control.KeyboardFocusable;
        protected override bool HasKeyboardFocusCore() => ReferenceEquals(_renderer.Input.FocusedTarget, _control);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
        protected override string GetItemStatusCore() => IsSelected ? "Selected" : ToggleButton is null ? "" : "Not selected";
        protected override string GetItemTypeCore() => "";
        protected override string GetLocalizedControlTypeCore() => ToggleButton is { FullRow: true } ? "switch" : base.GetLocalizedControlTypeCore();
        protected override string GetAcceleratorKeyCore() => "";
        protected override string GetAccessKeyCore() => "";
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override void SetFocusCore()
        {
            _renderer.Focus(); _renderer.Input.FocusAccessible(_control);
            var r = _control.GetInputRect().Rect; _renderer.BringIntoView(new Rect(r.Left, r.Top, r.Width, r.Height));
        }
        public void Invoke() => _renderer.Dispatcher.Invoke(() =>
        {
            if (!_control.ImmediateInputEnabled) throw new ElementNotEnabledException();
            if (!_renderer.ContentReady) return;
            SetFocusCore(); _renderer.HandleKeyDown(Key.Enter, ModifierKeys.None); _renderer.HandleKeyUp(Key.Enter);
        });
        public ToggleState ToggleState => IsSelected ? ToggleState.On : ToggleState.Off;
        public void Toggle() => Invoke();
        public bool IsSelected => Button?.IsSelected == true;
        public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(_parent);
        public void Select() { if (!IsSelected) Invoke(); }
        public void AddToSelection() => Select();
        public void RemoveFromSelection() { if (ToggleButton is { SingleChoice: true }) throw new InvalidOperationException("A deadline range must stay selected."); if (IsSelected) Invoke(); }
        public void Synchronize()
        {
            bool selected = IsSelected;
            if (selected == _selected) return;
            if (ToggleButton is { SingleChoice: true })
                RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, _selected, selected);
            else if (ToggleButton is not null)
                RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                    _selected ? ToggleState.On : ToggleState.Off, selected ? ToggleState.On : ToggleState.Off);
            else
                RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty,
                    _selected ? "Selected" : "", selected ? "Selected" : "");
            _selected = selected;
        }
    }
}
