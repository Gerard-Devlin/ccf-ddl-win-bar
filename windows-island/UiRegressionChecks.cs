using System.IO;
using System.Windows.Input;
using CcfDeadlineIsland.DynamicWinHost;
using CcfDeadlineIsland.Models;
using CcfDeadlineIsland.Services;
using DynamicWin.Main;
using DynamicWin.UI;
using DynamicWin.Utils;

namespace CcfDeadlineIsland;

// These checks traverse the same routed mouse handlers as WPF input. They deliberately
// do not invoke button callbacks or OnMouseUp directly; native capture is checked separately.
internal static class UiRegressionChecks
{
    public static int Run(RendererMain renderer)
    {
        var model = renderer.Model;
        var menu = renderer.Menu;
        var originalRows = model.Rows;
        var originalPreferences = model.Preferences;
        var originalSave = model.SavePreferences;
        var originalStartupAction = model.SetLaunchAtStartup;
        bool originalStartup = model.LaunchAtStartup;
        int originalSelection = model.SelectedIndex;
        string originalSelectedId = model.SelectedConferenceId;
        var conferences = originalRows.Select(row => row.Conference).ToArray();
        string folder = Path.Combine(Path.GetTempPath(), "ccf-island-ui-" + Guid.NewGuid().ToString("N"));
        string settings = Path.Combine(folder, "settings.json");
        int checks = 0, saves = 0;
        void Check(bool success, string description)
        {
            if (!success) throw new InvalidOperationException("Production UI: " + description);
            checks++;
        }
        UIObject Control(string id) => menu.Controls[id];
        Vec2 Center(string id)
        {
            var rect = Control(id).GetInputRect().Rect;
            return new Vec2(rect.MidX, rect.MidY);
        }
        void Page(string page) { menu.ShowPage(page); renderer.Settle(true); }
        bool ClickAt(Vec2 point)
        {
            bool captured = renderer.RoutePointerDown(point.X, point.Y);
            bool activated = renderer.RoutePointerUp(point.X, point.Y);
            return captured && activated;
        }
        void Click(string id) => Check(ClickAt(Center(id)), "routed click " + id);
        void Focus(string id)
        {
            for (int i = 0; i < menu.Controls.Count + 1 && !ReferenceEquals(renderer.Input.FocusedTarget, Control(id)); i++)
                renderer.Input.Key(Key.Tab);
            Check(ReferenceEquals(renderer.Input.FocusedTarget, Control(id)) && renderer.Input.HasKeyboardFocus, "Tab focus " + id);
        }
        try
        {
            model.Preferences = new Preferences();
            model.Rows = model.Preferences.Visible(conferences, DateTimeOffset.UtcNow);
            model.SetLaunchAtStartup = enabled => model.LaunchAtStartup = enabled;
            model.SavePreferences = () =>
            {
                saves++;
                if (!model.Preferences.Save(settings)) throw new InvalidOperationException("Cannot save temporary UI preferences");
                model.Rows = model.Preferences.Visible(conferences, DateTimeOffset.UtcNow);
            };

            Page("widgets");
            Check(menu.IslandSizeBig().X == 320 && menu.IslandSizeBig().Y == 218, "three-conference layout is 320x218 DIP");
            Check(!menu.UiObjects.Any(item => item.GetType().Name == "DWImageButton"), "no music transport controls");
            Check(!menu.Controls.ContainsKey("nav.filters") && !menu.Controls.ContainsKey("display.animations"), "settings replaces filter navigation and animation controls");
            Click("nav.settings"); renderer.Settle(true);
            Check(menu.Page == "settings" && menu.IslandSizeBig().Y == 432, "gear opens unified filters and startup settings");
            var rank = (ConferenceMenu.ToggleButton)Control("filters.rank.A");
            int beforeDowns = renderer.RoutedPointerDownCount, beforeUps = renderer.RoutedPointerUpCount;
            int beforeSaves = saves;
            // No render frame occurs between these events or between repeated clicks.
            Click("filters.rank.A");
            Check(!model.Preferences.Ccf.Contains("A") && model.Rows.Count == 0 && !rank.IsSelected, "quick rank click applies immediately and supports empty results");
            Click("filters.rank.A");
            for (int i = 0; i < 12; i++) Click("filters.rank.A");
            Check(model.Preferences.Ccf.Contains("A") && model.Rows.Count == conferences.Length && rank.IsSelected, "continuous rank switching retains exact state");
            Check(saves == beforeSaves + 14 && renderer.RoutedPointerDownCount == beforeDowns + 14 && renderer.RoutedPointerUpCount == beforeUps + 14, "one save per fast routed down/up pair");
            renderer.Input.PointerMove(Center("filters.rank.A")); rank.Update(1f / 60);
            Check(rank.IsSelected && rank.IsHovering && rank.hoverColor.a > rank.normalColor.a, "selected chip remains distinct while hovered");
            var rankPoint = Center("filters.rank.A");
            renderer.RoutePointerUp(rankPoint.X, rankPoint.Y);
            Check(saves == beforeSaves + 14, "uncaptured duplicate release does not activate");

            renderer.RoutePointerDown(rankPoint.X, rankPoint.Y);
            bool dragActivated = renderer.RoutePointerUp(0, 490);
            Check(!dragActivated && saves == beforeSaves + 14 && renderer.Input.CapturedTarget is null, "dragging outside before release cancels activation");
            renderer.RoutePointerDown(rankPoint.X, rankPoint.Y);
            Control("filters.rank.A").SetActive(false);
            Check(renderer.Input.CapturedTarget is null && !renderer.RoutePointerUp(rankPoint.X, rankPoint.Y), "disappearing control cancels capture immediately");
            menu.Update(); renderer.Settle(true);
            renderer.RoutePointerDown(rankPoint.X, rankPoint.Y);
            menu.ShowPage("widgets");
            Check(renderer.Input.CapturedTarget is null && !renderer.RoutePointerUp(rankPoint.X, rankPoint.Y), "page switch cancels capture");
            Page("settings");
            rankPoint = Center("filters.rank.A"); renderer.RoutePointerDown(rankPoint.X, rankPoint.Y);
            renderer.CancelInput();
            Check(renderer.Input.CapturedTarget is null && !renderer.RoutePointerUp(rankPoint.X, rankPoint.Y), "deactivation input cancellation prevents activation");

            var submissionRect = Control("filters.submission").GetInputRect().Rect;
            Check(ClickAt(new Vec2(submissionRect.Left + 210, submissionRect.MidY)), "checkbox text belongs to the click target");
            Check(model.Preferences.SubmissionOnly && model.Rows.All(row => row.Deadline.Kind == "submission"), "submission checkbox immediately changes deadlines");
            Click("filters.range.30");
            Check(model.Preferences.WithinDays == 30, "range choice applies immediately");
            var loaded = Preferences.Load(settings);
            Check(loaded.SubmissionOnly && loaded.WithinDays == 30 && loaded.Ccf.SequenceEqual(model.Preferences.Ccf), "filter choices restore from persisted preferences");

            renderer.Input.CancelAll(); Focus("filters.submission");
            bool beforeSubmission = model.Preferences.SubmissionOnly;
            renderer.Input.KeyDown(Key.Space); renderer.Input.KeyDown(Key.Space);
            Check(model.Preferences.SubmissionOnly == beforeSubmission, "key down and auto repeat do not activate before key up");
            renderer.Input.KeyUp(Key.Space);
            Check(model.Preferences.SubmissionOnly != beforeSubmission, "Space activates focused checkbox once");
            renderer.Input.Key(Key.Enter);
            Check(model.Preferences.SubmissionOnly == beforeSubmission, "Enter activates focused checkbox");
            var focused = renderer.Input.FocusedTarget;
            renderer.Input.Key(Key.Tab, ModifierKeys.Shift); renderer.Input.Key(Key.Tab);
            Check(ReferenceEquals(renderer.Input.FocusedTarget, focused), "Shift+Tab and Tab traverse controls in opposite directions");
            Click("filters.reset");
            Check(!model.Preferences.SubmissionOnly && model.Preferences.WithinDays == 365 && model.Preferences.Ccf.Count == 4, "reset restores filters");
            Click("filters.area.clear");
            Check(model.Preferences.Categories.Count == 0 && model.Rows.Count == 0, "clearing research areas produces an empty result");
            Click("filters.area.AI");
            Check(model.Preferences.Categories.SequenceEqual(new[] { "AI" }) && model.Rows.Count == 2
                && model.Rows.All(row => row.Conference.Area == "AI"), "research area filters conference data immediately");
            Check(Preferences.Load(settings).Categories.SequenceEqual(new[] { "AI" }), "research area selection survives preference reload");
            Click("filters.area.all");
            Check(model.Preferences.Categories.Count == Preferences.Areas.Count && model.Rows.Count == conferences.Length, "all research areas restores conferences");
            var startupToggle = (ConferenceMenu.ToggleButton)Control("settings.startup");
            var startupBounds = startupToggle.GetInputRect().Rect;
            Click("settings.startup");
            Check(model.LaunchAtStartup, "startup control calls the startup registration action");
            startupToggle.Update(1f / 60);
            Check(startupToggle.VisualScale < 1 && startupToggle.GetInputRect().Rect == startupBounds,
                "quick settings clicks animate without moving their hit area");
            Check(startupToggle.SelectionAmount > 0 && startupToggle.SelectionAmount < 1,
                "settings selection animates through intermediate states");
            renderer.Settle(true);
            Check(startupToggle.SelectionAmount > .999f && startupToggle.VisualScale > .99f,
                "settings press and selection animations settle");
            Click("settings.startup");
            Check(!model.LaunchAtStartup && DynamicWin.Main.Settings.AllowAnimation, "startup can be disabled while animation remains enabled");
            Check(ConferenceMenu.IsUrgent(TimeSpan.FromHours(24)) && !ConferenceMenu.IsUrgent(TimeSpan.FromHours(24) + TimeSpan.FromMilliseconds(1))
                && !ConferenceMenu.IsUrgent(TimeSpan.Zero), "urgent countdown threshold is positive and at most twenty-four hours");
            Check(ConferenceMenu.CountdownColor(TimeSpan.FromHours(18), Theme.TextMain).Value() == Theme.Error.Value()
                && ConferenceMenu.CountdownColor(TimeSpan.FromDays(2), Theme.TextMain).Value() == Theme.TextMain.Value(), "only imminent countdowns use red");

            Page("widgets");
            string selectedId = ((ConferenceMenu.ConferenceRow)Control("upcoming.row.1")).BoundId;
            Click("upcoming.row.1"); renderer.Settle(true);
            Check(menu.Page == "details" && menu.Selected?.Id == selectedId, "conference row opens its own deadline details");
            Check(Control("details.website").ImmediateInputEnabled, "explicit website action is available in details");
            Click("details.back"); renderer.Settle(true);
            Check(menu.Page == "widgets", "details Back restores origin page");
            renderer.Settle(false);
            var chosen = model.Rows.Single(row => row.Id == selectedId);
            Check(menu.UiObjects.OfType<DynamicWin.UI.UIElements.DWText>().Any(text => text.Text == chosen.Conference.Title)
                && menu.Selected?.Id == selectedId, "compact title follows the selected conference instead of the first row");
            model.Rows = model.Rows.Reverse().ToArray(); menu.Update();
            Check(menu.Selected?.Id == selectedId && model.Rows[model.SelectedIndex].Id == selectedId,
                "selected conference survives data reordering by stable ID");
            model.Rows = model.Preferences.Visible(conferences, DateTimeOffset.UtcNow); renderer.Settle(true);

            var rowPoint = Center("upcoming.row.0");
            renderer.RoutePointerDown(rowPoint.X, rowPoint.Y);
            model.Rows = model.Rows.Skip(1).ToArray(); menu.Update(); renderer.Input.Validate();
            Check(renderer.Input.CapturedTarget is null && !renderer.RoutePointerUp(rowPoint.X, rowPoint.Y) && menu.Page == "widgets", "rebound conference slot cancels pending click");

            var now = DateTimeOffset.UtcNow;
            var soon = new DeadlinePoint("abstract", "soon", "UTC", now.AddSeconds(1), null);
            var later = new DeadlinePoint("submission", "later", "UTC", now.AddDays(2), null);
            var expiring = conferences[0] with { Id = "expiry", Deadlines = new() { soon, later } };
            var other = conferences[1] with { Id = "after-expiry", Deadlines = new() { later with { Utc = now.AddDays(1) } } };
            var beforeExpiry = new Preferences().Visible(new[] { expiring, other }, now);
            var afterExpiry = new Preferences().Visible(new[] { expiring, other }, now.AddSeconds(2));
            Check(beforeExpiry[0].Id == "expiry" && afterExpiry[0].Id == "after-expiry" && afterExpiry.All(row => row.Deadline.Utc > now.AddSeconds(2)), "expired deadlines are removed and future meetings promoted");

            for (int count = 0; count <= 3; count++)
            {
                model.Rows = new Preferences().Visible(conferences.Take(count), now);
                foreach (string page in new[] { "widgets", "list" })
                {
                    Page(page);
                    string prefix = page == "widgets" ? "upcoming.row." : "list.row.";
                    int slots = page == "widgets" ? 3 : 5;
                    Check(Enumerable.Range(0, slots).Count(index => Control(prefix + index).ImmediateInputEnabled) == count, page + ": " + count + " conferences expose exactly the available rows");
                    float expected = count == 0 ? 146 : 62 + 52 * count;
                    Check(menu.IslandSizeBig().Y == expected, page + ": " + count + " conferences use compact non-overlapping geometry");
                    var island = renderer.MainIsland.GetRect().Rect;
                    for (int index = 0; index < count; index++)
                    {
                        var bounds = Control(prefix + index).GetInputRect().Rect;
                        Check(bounds.Left >= island.Left && bounds.Right <= island.Right && bounds.Top >= island.Top + 48 && bounds.Bottom <= island.Bottom - 16, "row stays inside shell with bottom padding");
                    }
                }
            }
            var longTitle = conferences[0] with { Id = "long-title", Title = "International Conference on a Very Long Conference Name for Layout Verification" };
            model.Rows = new Preferences().Visible(new[] { longTitle }, now); Page("widgets");
            Check(((ConferenceMenu.ConferenceRow)Control("upcoming.row.0")).BoundId == "long-title" && menu.IslandSizeBig().X == 320, "long conference names retain fixed width and binding");

            var points = Enumerable.Range(0, 6).Select(index => new DeadlinePoint("submission", "cycle-" + index, "UTC", now.AddDays(index + 10), null)).ToList();
            var manyDates = conferences[0] with { Id = "many-dates", Deadlines = points };
            model.Rows = new Preferences().Visible(new[] { manyDates }, now); Page("widgets"); Click("upcoming.row.0"); renderer.Settle(true);
            Check(Control("details.next").ImmediateInputEnabled && !Control("details.previous").ImmediateInputEnabled, "deadline pagination starts at first dates");
            Click("details.next"); renderer.Settle(true); Click("details.next"); renderer.Settle(true);
            var visibleDates = menu.UiObjects.Where(item => item.ImmediateInputEnabled).Select(item => item.GetType().GetProperty("Point")?.GetValue(item)).OfType<DeadlinePoint>().ToList();
            Check(visibleDates.Select(point => point.SourceTime).SequenceEqual(new[] { "cycle-4", "cycle-5" }) && !Control("details.next").ImmediateInputEnabled, "all later deadline cycles are reachable without clipping");
            Click("details.previous"); renderer.Settle(true);
            Check(Control("details.next").ImmediateInputEnabled, "deadline pagination can move backwards");

            var allConferences = Enumerable.Range(0, 8).Select(index => conferences[0] with
            {
                Id = "scroll-" + index, Title = "Conference " + index,
                Deadlines = new() { later with { Utc = now.AddDays(index + 2) } }
            }).ToArray();
            model.Rows = new Preferences().Visible(allConferences, now); Page("list");
            Check(menu.IslandSizeBig().Y == 322, "all-conferences page has separate scrollable height");
            Check(((ConferenceMenu.ConferenceRow)Control("list.row.0")).BoundId == "scroll-0", "all-conferences list starts at first row");
            menu.Scroll(-120); renderer.Settle(true);
            Check(((ConferenceMenu.ConferenceRow)Control("list.row.0")).BoundId == "scroll-1", "all-conferences list scrolls through later rows");
            for (int i = 0; i < 10; i++) menu.Scroll(-120);
            renderer.Settle(true);
            Check(((ConferenceMenu.ConferenceRow)Control("list.row.4")).BoundId == "scroll-7", "all-conferences scrolling reaches last row and clamps");
            Check(ConferenceMenu.Remaining(TimeSpan.Zero) == "Expired" && !ConferenceMenu.Remaining(TimeSpan.FromDays(12)).Contains("s"), "countdown avoids stale zero seconds and distant second updates");
            renderer.ForceExpanded = false; renderer.ForceCollapsed = true;
            Check(!renderer.TryReleaseDismissal(new Vec2(0, 490)) && renderer.ForceCollapsed,
                "dismissal is retained throughout the contraction animation");
            renderer.Settle(false);
            Check(!renderer.TryReleaseDismissal(new Vec2(350, 20)) && renderer.ForceCollapsed,
                "Esc dismissal stays active while the cursor is over the compact island");
            Check(renderer.TryReleaseDismissal(new Vec2(350, 120)) && !renderer.ForceCollapsed,
                "hover can resume after contraction completes and the cursor leaves");
            foreach (string centeredPage in new[] { "widgets", "settings", "compact" })
            {
                menu.ShowPage(centeredPage == "compact" ? "widgets" : centeredPage);
                renderer.Settle(centeredPage != "compact");
                Check(Math.Abs(renderer.MainIsland.GetRect().Rect.MidX - 350) < .001f,
                    centeredPage + " shell remains geometrically centered after spring settling");
            }
            Check(!renderer.RoutePointerDown(350, 16) && !renderer.RoutePointerUp(350, 16) && menu.Page == "widgets",
                "opening click on the compact shell cannot activate moving navigation controls");
            return checks;
        }
        finally
        {
            renderer.Input.CancelAll();
            model.Rows = originalRows; model.Preferences = originalPreferences; model.SavePreferences = originalSave;
            model.LaunchAtStartup = originalStartup; model.SetLaunchAtStartup = originalStartupAction;
            model.SelectedIndex = originalSelection;
            model.SelectedConferenceId = originalSelectedId;
            menu.ShowPage("widgets"); renderer.Settle(true);
            if (File.Exists(settings)) File.Delete(settings);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
