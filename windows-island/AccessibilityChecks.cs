using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using CcfDeadlineIsland.DynamicWinHost;
using CcfDeadlineIsland.Models;
using DynamicWin.Main;
using DynamicWin.Utils;

namespace CcfDeadlineIsland;

internal static class AccessibilityChecks
{
    public static int Run(RendererMain renderer)
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException("Accessibility: " + message); checks++; }
        var rows = renderer.Model.Rows;
        var selected = renderer.Model.SelectedConferenceId;
        bool animation = Settings.AllowAnimation;
        int days = renderer.Model.Preferences.WithinDays;
        try
        {
            renderer.Menu.ShowPage("settings"); renderer.Settle(true);
            var peer = UIElementAutomationPeer.CreatePeerForElement(renderer);
            Check(peer is IslandAutomationPeer, "custom surface exposes a native automation peer");
            var children = peer.GetChildren();
            var ai = children.Single(p => p.GetAutomationId() == "filters.area.AI");
            Check(ai.GetName() == "Artificial intelligence" && ai.GetHelpText() == "Artificial intelligence", "abbreviations have expanded names");
            var toggle = children.Single(p => p.GetAutomationId() == "filters.submission");
            Check(toggle.GetPattern(PatternInterface.Toggle) is IToggleProvider && toggle.GetLocalizedControlType() == "switch", "switch exposes native toggle semantics");
            var range = children.Single(p => p.GetAutomationId() == "filters.range.30");
            Check(range.GetAutomationControlType() == AutomationControlType.RadioButton && range.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider,
                "range uses mutually exclusive selection semantics");
            ((ISelectionItemProvider)range.GetPattern(PatternInterface.SelectionItem)).Select();
            Check(renderer.Model.Preferences.WithinDays == 30 && renderer.Input.FocusedTarget?.InputIdentity == "filters.range.30", "automation invokes the production keyboard path");
            Check(((ISelectionProvider)peer.GetPattern(PatternInterface.Selection)).GetSelection().Length == 1, "selection container reports one selected range");
            Check(!renderer.Menu.Controls.Keys.Any(id => id is "nav.filters" or "settings.done" or "settings.reset" or "display.animations"), "removed controls remain absent");
            Check(ConferenceMenu.CountdownWidth("11d 11h", 14) == ConferenceMenu.CountdownWidth("88d 88h", 14), "Inter countdown digit cells have identical advances");
            var row = rows[0];
            renderer.Model.SelectedConferenceId = row.Id;
            foreach (double seconds in new[] { 59, 60, 599, 600, 3599, 3600, 86399, 86400, 864000, 8640000 })
            {
                renderer.Model.Rows = new[] { row with { Deadline = row.Deadline with { Utc = DateTimeOffset.UtcNow.AddSeconds(seconds) } } };
                renderer.Menu.ShowPage("widgets"); renderer.Settle(false);
                Check(renderer.Menu.IslandSize().X == 224, "countdown unit boundary keeps compact width fixed");
            }
            renderer.Model.Rows = new[] { row with { Conference = row.Conference with { Title = "International Conference on Software Engineering and Systems" } } };
            renderer.Menu.ShowPage("details"); renderer.Settle(true);
            Check(renderer.Menu.AccessibleSummary.Contains("Software Engineering and Systems"), "full long title and dates are exposed to accessibility");
            Check(renderer.Menu.IslandSizeBig().Y > 297, "long detail title gets additional height instead of truncation");
            Settings.AllowAnimation = false;
            renderer.Menu.ShowPage("settings"); renderer.Settle(true, 1);
            Check(renderer.ContentReady, "reduced motion snaps shell to its final geometry");
            var button = (ConferenceMenu.ToggleButton)renderer.Menu.Controls["filters.rank.A"];
            Check(button.VisualScale == 1 && button.SelectionAmount == (button.IsSelected ? 1 : 0), "reduced motion disables control bounce and selection transitions");
            double Channel(byte v) => v / 255.0 <= .04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + .055) / 1.055, 2.4);
            double Luminance(SkiaSharp.SKColor c) => .2126 * Channel(c.Red) + .7152 * Channel(c.Green) + .0722 * Channel(c.Blue);
            foreach (byte background in new byte[] { 0, 17, 23, 33 })
                Check((Luminance(Theme.Error.Value()) + .05) / (Luminance(new SkiaSharp.SKColor(background, background, background)) + .05) >= 4.5, "urgent text meets AA on normal, hover and pressed cards");
            return checks;
        }
        finally
        {
            renderer.Model.Rows = rows; renderer.Model.SelectedConferenceId = selected;
            renderer.Model.Preferences.WithinDays = days; Settings.AllowAnimation = animation;
            renderer.Menu.ClearFeedback(); renderer.Input.CancelAll(); renderer.Menu.ShowPage("widgets"); renderer.Settle(true);
        }
    }
}
