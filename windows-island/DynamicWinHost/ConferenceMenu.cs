// CCF content and interaction adaptation of Florian Butz's DynamicWin main UI.
// Original black shell, Inter fonts, palette and spring classes remain in Upstream/DynamicWin.
// Source: 9f24d176a16333b1cfda21dd3ed48087497194ed, CC BY-SA 4.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CcfDeadlineIsland.Models;
using CcfDeadlineIsland.Services;
using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using SkiaSharp;

namespace CcfDeadlineIsland.DynamicWinHost;

public interface IIslandMenu
{
    Vec2 IslandSize();
    Vec2 IslandSizeBig();
    Col IslandBorderColor();
    List<UIObject> UiObjects { get; }
    void Update();
}

public sealed class ConferenceModel
{
    public IReadOnlyList<DeadlineRow> Rows { get; set; } = Array.Empty<DeadlineRow>();
    public int SelectedIndex { get; set; }
    public Preferences Preferences { get; set; } = new();
    public string Status { get; set; } = "Loading...";
    public bool Loading { get; set; }
    public Action? Refresh, OpenWebsite, ExportCalendar, CopyTime, SavePreferences, TogglePin;
}

public sealed class ConferenceMenu : IIslandMenu
{
    private readonly IslandObject _island;
    private readonly ConferenceModel _model;
    private readonly UIObject _compactContainer;
    private readonly DWText _compactTitle;
    private readonly DWText _compactCountdown;
    private readonly Dictionary<string, List<UIObject>> _pages = new();
    private readonly List<UIObject> _navigation = new();
    private readonly Dictionary<string, MenuButton> _buttons = new();
    private readonly ConferenceRow[] _upcomingRows = new ConferenceRow[3];
    private readonly ConferenceRow[] _listRows = new ConferenceRow[5];
    private readonly Dictionary<string, ToggleButton> _ranks = new();
    private readonly Dictionary<int, ToggleButton> _ranges = new();
    private readonly ToggleButton _submission;
    private readonly ToggleButton _animations;
    private readonly Label _empty;
    private readonly Label _detailTitle;
    private readonly Label _detailPage;
    private readonly DeadlinePanel[] _detailPanels = new DeadlinePanel[2];
    private readonly MenuButton _emptyAction;
    private readonly MenuButton _previousDeadlines;
    private readonly MenuButton _nextDeadlines;
    private string _selectedId = "";
    private string _returnPage = "widgets";
    private string _detailsReturnPage = "widgets";
    private int _listOffset;
    private int _detailOffset;
    private int _detailTotal;

    public List<UIObject> UiObjects { get; } = new();
    public Dictionary<string, UIObject> Controls { get; } = new();
    public string Page { get; private set; } = "widgets";
    public Action<string>? PageChanged { get; set; }
    public DeadlineRow? Selected => Page == "details" && _selectedId.Length > 0
        ? _model.Rows.FirstOrDefault(row => row.Id == _selectedId)
        : _model.Rows.ElementAtOrDefault(Math.Clamp(_model.SelectedIndex, 0, Math.Max(0, _model.Rows.Count - 1)));

    public ConferenceMenu(IslandObject island, ConferenceModel model)
    {
        _island = island;
        _model = model;
        foreach (string page in new[] { "widgets", "list", "details", "filters", "display" }) _pages[page] = new();
        _compactContainer = new UIObject(island, Vec2.zero, IslandSize() - 35, UIAlignment.Center) { Color = Col.Transparent };
        _compactTitle = new DWText(_compactContainer, "CCF", Vec2.zero, UIAlignment.MiddleLeft)
            { Anchor = new Vec2(0, 0.5f), TextSize = 14, Font = Res.InterRegular, Color = Theme.TextMain };
        _compactCountdown = new DWText(_compactContainer, "--", Vec2.zero, UIAlignment.MiddleRight)
            { Anchor = new Vec2(1, 0.5f), TextSize = 14, Font = Res.InterRegular, Color = Theme.TextSecond };
        UiObjects.Add(_compactTitle); UiObjects.Add(_compactCountdown);

        Nav("nav.upcoming", "Upcoming", 14, 84, () => ShowPage("widgets"));
        Nav("nav.all", "All", 102, 40, () => ShowPage("list"));
        Nav("nav.filters", "Filter", 202, 54, () => ShowPage("filters"));
        var display = Button("nav.display", "", 272, 14, 28, 26, () => ShowPage("display"));
        display.Icon = Res.Settings; display.ActionName = "Display settings"; _navigation.Add(display);

        for (int slot = 0; slot < 3; slot++)
        {
            var row = new ConferenceRow(island, 16, 50 + slot * 52, id => OpenDetails(id, "widgets"));
            Register($"upcoming.row.{slot}", row); _upcomingRows[slot] = row;
        }
        for (int slot = 0; slot < 5; slot++)
        {
            var row = new ConferenceRow(island, 16, 50 + slot * 52, id => OpenDetails(id, "list"));
            Register($"list.row.{slot}", row); _listRows[slot] = row;
        }
        _empty = new Label(island, 16, 65, 288, 24, 13, Theme.TextSecond) { Text = "No matching conferences", Center = true };
        UiObjects.Add(_empty);
        _emptyAction = Button("empty.action", "Reset filters", 99, 102, 122, 28, () =>
        {
            if (HasFilters()) ResetFilters(); else _model.Refresh?.Invoke();
        });

        AddLabel("filters", "CCF rank", 16, 50, 288, 18, 11, Theme.TextSecond);
        for (int index = 0; index < Preferences.CcfValues.Length; index++)
        {
            string rank = Preferences.CcfValues[index];
            var chip = new ToggleButton(island, rank == "N" ? "Unranked" : rank, 16 + index * 74, 73, 66, 30, () =>
            {
                if (_model.Preferences.Ccf.Contains(rank)) _model.Preferences.Ccf.Remove(rank);
                else _model.Preferences.Ccf.Add(rank);
                SaveAndRefresh();
            });
            chip.FontSize = rank == "N" ? 9.5f : 11; Register("filters.rank." + rank, chip, "filters"); _ranks[rank] = chip;
        }
        AddLabel("filters", "Deadline range", 16, 114, 288, 18, 11, Theme.TextSecond);
        int[] days = { 30, 90, 180, 365 };
        for (int index = 0; index < days.Length; index++)
        {
            int range = days[index];
            var chip = new ToggleButton(island, range + " days", 16 + index * 74, 137, 66, 30, () =>
            { _model.Preferences.WithinDays = range; SaveAndRefresh(); });
            chip.FontSize = 10; Register("filters.range." + range, chip, "filters"); _ranges[range] = chip;
        }
        _submission = new ToggleButton(island, "Submission deadlines only", 16, 188, 288, 36, () =>
        { _model.Preferences.SubmissionOnly = !_model.Preferences.SubmissionOnly; SaveAndRefresh(); }) { FullRow = true, FontSize = 12 };
        Register("filters.submission", _submission, "filters");
        AddPageButton("filters", "filters.reset", "Reset filters", 16, 244, 114, 28, ResetFilters);
        AddPageButton("filters", "filters.done", "Done", 236, 244, 68, 28, () => ShowPage(_returnPage));

        AddLabel("display", "Display", 16, 51, 288, 23, 15, Theme.TextMain, bold: true);
        _animations = new ToggleButton(island, "Animations", 16, 83, 288, 36, () =>
        { _model.Preferences.ReducedMotion = !_model.Preferences.ReducedMotion; SaveAndRefresh(); }) { FullRow = true, FontSize = 12 };
        Register("display.animations", _animations, "display");
        AddPageButton("display", "display.done", "Done", 236, 136, 68, 28, () => ShowPage(_returnPage));

        AddPageButton("details", "details.back", "Back", 16, 50, 50, 25, () => ShowPage(_detailsReturnPage));
        _detailTitle = AddLabel("details", "", 76, 50, 228, 25, 14, Theme.TextMain, bold: true);
        for (int i = 0; i < 2; i++)
        {
            _detailPanels[i] = new DeadlinePanel(island, 16, 87 + i * 83, 288, 75);
            UiObjects.Add(_detailPanels[i]);
        }
        AddPageButton("details", "details.website", "Website", 16, 267, 100, 28, () => InvokeSelected(_model.OpenWebsite));
        AddPageButton("details", "details.copy", "Copy times", 124, 267, 78, 28, () => InvokeSelected(_model.CopyTime));
        AddPageButton("details", "details.calendar", "Calendar", 210, 267, 94, 28, () => InvokeSelected(_model.ExportCalendar));
        _previousDeadlines = Button("details.previous", "<", 16, 313, 30, 24, () => Scroll(120));
        _previousDeadlines.ActionName = "Previous deadline dates";
        _nextDeadlines = Button("details.next", ">", 274, 313, 30, 24, () => Scroll(-120));
        _nextDeadlines.ActionName = "Next deadline dates";
        _detailPage = AddLabel("details", "", 53, 313, 214, 24, 10, Theme.TextThird, center: true);

        foreach (var item in UiObjects) item.SilentSetActive(false);
    }

    public Vec2 IslandSize()
    {
        var row = _model.Rows.FirstOrDefault();
        string title = row?.Conference.Title ?? "CCF";
        string remaining = row is null ? (_model.Loading ? "Loading" : "--") : Remaining(row.Deadline.Utc - DateTimeOffset.UtcNow);
        using var paint = new SKPaint { Typeface = Res.InterRegular, TextSize = 14 };
        return new Vec2(Math.Max(200, paint.MeasureText(DWText.Truncate(title, 12)) + paint.MeasureText(remaining) + 57.5f), 35);
    }
    public Vec2 IslandSizeBig() => new(320, Page switch
    {
        "filters" => 288, "display" => 180, "list" => 322, "details" => 352,
        _ => _model.Rows.Count == 0 ? 146 : 62 + 52 * Math.Min(3, _model.Rows.Count)
    });
    public Col IslandBorderColor() => Col.Transparent;

    public void ShowPage(string page)
    {
        page = page switch { "upcoming" => "widgets", "settings" => "filters", _ => page };
        if (!_pages.ContainsKey(page)) page = "widgets";
        if (page == "details" && Selected is null) page = "widgets";
        if (Page == page) { Update(); return; }
        if (page is "filters" or "display") _returnPage = Page is "widgets" or "list" or "details" ? Page : _returnPage;
        string selectedId = Selected?.Id ?? "";
        Page = page;
        if (page == "details") { _selectedId = selectedId; _detailOffset = 0; }
        PageChanged?.Invoke(page);
        Update();
    }
    public void Scroll(int delta)
    {
        if (delta == 0) return;
        if (Page == "list") _listOffset = Math.Clamp(_listOffset + (delta > 0 ? -1 : 1), 0, Math.Max(0, _model.Rows.Count - 5));
        if (Page == "details") _detailOffset = Math.Clamp(_detailOffset + (delta > 0 ? -2 : 2), 0, Math.Max(0, _detailTotal - 2));
        Update();
    }
    public void SelectRelative(int delta)
    {
        if (_model.Rows.Count == 0) return;
        _model.SelectedIndex = ((_model.SelectedIndex + delta) % _model.Rows.Count + _model.Rows.Count) % _model.Rows.Count;
        if (Page == "details") { _selectedId = _model.Rows[_model.SelectedIndex].Id; _detailOffset = 0; }
        Update();
    }

    public void Update()
    {
        bool expanded = _island.IsHovering;
        if (_model.Rows.Count > 0) _model.SelectedIndex = Math.Clamp(_model.SelectedIndex, 0, _model.Rows.Count - 1);
        if (Page == "details" && _selectedId.Length > 0 && !_model.Rows.Any(row => row.Id == _selectedId))
        { Page = "widgets"; _selectedId = ""; PageChanged?.Invoke(Page); }
        var next = _model.Rows.FirstOrDefault();
        _compactContainer.Size = IslandSize() - 35;
        _compactTitle.SilentSetText(next is null ? "CCF" : DWText.Truncate(next.Conference.Title, 12));
        _compactCountdown.SilentSetText(next is null ? (_model.Loading ? "Loading" : "--") : Remaining(next.Deadline.Utc - DateTimeOffset.UtcNow));
        _compactTitle.SetActive(!expanded); _compactCountdown.SetActive(!expanded);
        foreach (var group in _pages)
            foreach (var item in group.Value) item.SetActive(expanded && group.Key == Page);
        foreach (var item in _navigation) item.SetActive(expanded);
        _buttons["nav.upcoming"].IsSelected = Page == "widgets";
        _buttons["nav.all"].IsSelected = Page == "list";
        _buttons["nav.filters"].IsSelected = Page == "filters";
        _buttons["nav.display"].IsSelected = Page == "display";
        for (int i = 0; i < 3; i++)
        {
            _upcomingRows[i].Bind(_model.Rows.ElementAtOrDefault(i));
            _upcomingRows[i].SetActive(expanded && Page == "widgets" && _upcomingRows[i].Row is not null);
        }
        _listOffset = Math.Clamp(_listOffset, 0, Math.Max(0, _model.Rows.Count - 5));
        for (int i = 0; i < 5; i++)
        {
            _listRows[i].Bind(_model.Rows.ElementAtOrDefault(_listOffset + i));
            _listRows[i].SetActive(expanded && Page == "list" && _listRows[i].Row is not null);
        }
        foreach (var pair in _ranks) pair.Value.IsSelected = _model.Preferences.Ccf.Contains(pair.Key);
        foreach (var pair in _ranges) pair.Value.IsSelected = _model.Preferences.WithinDays == pair.Key;
        _submission.IsSelected = _model.Preferences.SubmissionOnly;
        _animations.IsSelected = !_model.Preferences.ReducedMotion;
        bool listPage = Page is "widgets" or "list";
        bool empty = _model.Rows.Count == 0;
        _empty.SetActive(expanded && listPage && empty);
        _empty.Text = _model.Loading ? "Loading conferences..." : HasFilters() ? "No matching conferences" : "No upcoming conferences";
        _emptyAction.SetActive(expanded && listPage && empty && !_model.Loading);
        _emptyAction.Text = HasFilters() ? "Reset filters" : "Retry";
        UpdateDetails(expanded);
    }

    private void UpdateDetails(bool expanded)
    {
        var selected = Page == "details" ? Selected : null;
        var points = selected?.Conference.Deadlines.Where(point => point.Utc > DateTimeOffset.UtcNow).OrderBy(point => point.Utc).ToList() ?? new();
        _detailTotal = points.Count;
        _detailOffset = Math.Clamp(_detailOffset, 0, Math.Max(0, points.Count - 2));
        _detailTitle.Text = selected?.Heading ?? "";
        for (int i = 0; i < 2; i++)
        {
            var point = points.ElementAtOrDefault(_detailOffset + i);
            _detailPanels[i].Point = point;
            _detailPanels[i].SetActive(expanded && Page == "details" && point is not null);
        }
        foreach (string id in new[] { "details.website", "details.copy", "details.calendar" })
        {
            _buttons[id].InputIdentity = id + ":" + (selected?.Id ?? "");
            _buttons[id].AcceptsInput = selected is not null;
        }
        _buttons["details.website"].AcceptsInput = selected is not null && Uri.TryCreate(selected.Conference.Website, UriKind.Absolute, out var website) && website.Scheme is "http" or "https";
        _previousDeadlines.AcceptsInput = _detailOffset > 0;
        _nextDeadlines.AcceptsInput = _detailOffset + 2 < points.Count;
        _previousDeadlines.SetActive(expanded && Page == "details" && points.Count > 2);
        _nextDeadlines.SetActive(expanded && Page == "details" && points.Count > 2);
        _detailPage.Text = points.Count == 0 ? "No future deadlines" : $"{_detailOffset + 1}-{Math.Min(points.Count, _detailOffset + 2)} / {points.Count} deadline dates";
    }
    private void OpenDetails(string id, string fromPage)
    {
        int index = _model.Rows.ToList().FindIndex(row => row.Id == id && row.Deadline.Utc > DateTimeOffset.UtcNow);
        if (index < 0) return;
        _model.SelectedIndex = index; _selectedId = id; _detailOffset = 0; _detailsReturnPage = fromPage;
        Page = "details"; PageChanged?.Invoke(Page); Update();
    }
    private void InvokeSelected(Action? action)
    {
        int index = _model.Rows.ToList().FindIndex(row => row.Id == _selectedId && row.Deadline.Utc > DateTimeOffset.UtcNow);
        if (index < 0) { ShowPage("widgets"); return; }
        _model.SelectedIndex = index; action?.Invoke();
    }
    private bool HasFilters() => _model.Preferences.Ccf.Count != Preferences.CcfValues.Length || _model.Preferences.WithinDays != 365
        || _model.Preferences.SubmissionOnly || _model.Preferences.Core.Count != Preferences.CoreValues.Length
        || _model.Preferences.Thcpl.Count != Preferences.ThcplValues.Length || _model.Preferences.Categories.Count != Preferences.Areas.Count;
    private void ResetFilters()
    {
        var preferences = _model.Preferences;
        preferences.Ccf = new(Preferences.CcfValues); preferences.Core = new(Preferences.CoreValues); preferences.Thcpl = new(Preferences.ThcplValues);
        preferences.Categories = new(Preferences.Areas.Keys); preferences.WithinDays = 365; preferences.SubmissionOnly = false; preferences.Search = "";
        SaveAndRefresh();
    }
    private void SaveAndRefresh() { _model.SavePreferences?.Invoke(); Update(); }
    public static string Remaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "Expired";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        long seconds = (long)Math.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60}m {seconds % 60}s";
    }

    private void Nav(string id, string text, float x, float width, Action action)
    { var button = Button(id, text, x, 14, width, 26, action); button.FontSize = 11; _navigation.Add(button); }
    private MenuButton Button(string id, string text, float x, float y, float width, float height, Action action)
    { var button = new MenuButton(_island, text, x, y, width, height, action); Register(id, button); return button; }
    private MenuButton AddPageButton(string page, string id, string text, float x, float y, float width, float height, Action action)
    { var button = Button(id, text, x, y, width, height, action); _pages[page].Add(button); return button; }
    private void Register(string id, MenuButton button, string? page = null)
    {
        button.InputIdentity = id; button.StableId = id; Controls[id] = button; _buttons[id] = button; UiObjects.Add(button);
        if (page is not null) _pages[page].Add(button);
    }
    private void AddPageObject(string page, UIObject item) { UiObjects.Add(item); _pages[page].Add(item); }
    private Label AddLabel(string page, string text, float x, float y, float width, float height, float size, Col color, bool bold = false, bool center = false)
    { var label = new Label(_island, x, y, width, height, size, color) { Text = text, Bold = bold, Center = center }; AddPageObject(page, label); return label; }

    public class MenuButton : DWButton
    {
        public string StableId { get; set; } = "";
        public string ActionName { get; set; }
        public string Text { get; set; }
        public float FontSize { get; set; } = 11.5f;
        public bool IsSelected { get; set; }
        public SKBitmap? Icon { get; set; }
        public MenuButton(IslandObject island, string text, float x, float y, float width, float height, Action action)
            : base(island, new Vec2(x, y), new Vec2(width, height), action, UIAlignment.TopLeft)
        {
            Anchor = Vec2.zero; Text = text; ActionName = text;
            roundRadius = 8; normalScaleMulti = hoverScaleMulti = clickScaleMulti = Vec2.one;
            normalColor = Col.Transparent; hoverColor = Col.White.Override(a: 0.08f); clickColor = Col.White.Override(a: 0.14f);
        }
        public override void Update(float deltaTime)
        {
            normalColor = IsSelected ? Col.White.Override(a: 0.075f) : Col.Transparent;
            hoverColor = IsSelected ? Col.White.Override(a: 0.12f) : Col.White.Override(a: 0.08f);
            base.Update(deltaTime);
        }
        public override void Draw(SKCanvas canvas)
        {
            using var background = GetPaint(); background.Color = Color.Value(); canvas.DrawRoundRect(GetRect(), background);
            var foreground = GetColor(AcceptsInput ? Theme.TextSecond : Theme.TextThird);
            if (Icon is not null)
            {
                using var paint = GetPaint(); paint.Color = foreground.Value();
                canvas.DrawBitmap(Icon, SKRect.Create(Position.X + (Size.X - 15) / 2, Position.Y + (Size.Y - 15) / 2, 15, 15), paint);
            }
            else DrawText(canvas, Text, Position.X + Size.X / 2, Position.Y + Size.Y / 2, FontSize, foreground, centered: true, middle: true, maxWidth: Size.X - 12);
        }
    }
    public sealed class ToggleButton : MenuButton
    {
        public bool FullRow { get; set; }
        public ToggleButton(IslandObject island, string text, float x, float y, float width, float height, Action action) : base(island, text, x, y, width, height, action) { }
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            normalColor = IsSelected ? Theme.Primary.Override(a: 0.34f) : Theme.WidgetBackground;
            hoverColor = IsSelected ? Theme.Primary.Override(a: 0.48f) : Col.White.Override(a: 0.12f);
            clickColor = IsSelected ? Theme.Primary.Override(a: 0.58f) : Col.White.Override(a: 0.18f);
        }
        public override void Draw(SKCanvas canvas)
        {
            using var background = GetPaint(); background.Color = GetColor(IsHovering ? hoverColor : normalColor).Value(); canvas.DrawRoundRect(GetRect(), background);
            float checkX = Position.X + (FullRow ? 14 : 9);
            if (FullRow || IsSelected)
            {
                if (FullRow)
                {
                    using var box = GetPaint(); box.Color = GetColor(IsSelected ? Theme.Primary : Theme.TextThird).Value(); box.IsStroke = !IsSelected; box.StrokeWidth = 1;
                    canvas.DrawRoundRect(SKRect.Create(checkX - 2, Position.Y + (Size.Y - 16) / 2, 16, 16), 4, 4, box);
                }
                if (IsSelected)
                {
                    using var tick = GetPaint(); tick.Color = GetColor(Theme.TextMain).Value(); tick.IsStroke = true; tick.StrokeWidth = 1.5f; tick.StrokeCap = SKStrokeCap.Round;
                    float cy = Position.Y + Size.Y / 2; using var path = new SKPath();
                    path.MoveTo(checkX + (FullRow ? 2 : -1), cy); path.LineTo(checkX + (FullRow ? 5 : 2), cy + 3); path.LineTo(checkX + (FullRow ? 10 : 7), cy - 3); canvas.DrawPath(path, tick);
                }
            }
            if (FullRow) DrawText(canvas, Text, Position.X + 40, Position.Y + Size.Y / 2, FontSize, GetColor(IsSelected ? Theme.TextMain : Theme.TextSecond), middle: true, maxWidth: Size.X - 52);
            else DrawText(canvas, Text, Position.X + Size.X / 2 + (IsSelected ? 5 : 0), Position.Y + Size.Y / 2, FontSize, GetColor(IsSelected ? Theme.TextMain : Theme.TextSecond), centered: true, middle: true, maxWidth: Size.X - (IsSelected ? 18 : 12));
        }
    }
    public sealed class ConferenceRow : MenuButton
    {
        private readonly Action<string> _select;
        public DeadlineRow? Row { get; private set; }
        public string BoundId => Row?.Id ?? "";
        public ConferenceRow(IslandObject island, float x, float y, Action<string> select) : base(island, "", x, y, 288, 48, () => { })
        {
            _select = select; roundRadius = 11;
            clickCallback = () => { if (Row is not null) _select(Row.Id); };
        }
        public void Bind(DeadlineRow? row)
        {
            Row = row; AcceptsInput = row is not null; InputIdentity = StableId + ":" + (row?.Id ?? "");
            ActionName = row is null ? "" : "Deadline details for " + row.Heading;
        }
        public override void Draw(SKCanvas canvas)
        {
            if (Row is not { } row) return;
            using var paint = GetPaint(); paint.Color = GetColor(IsMouseDown ? Col.White.Override(a: 0.13f) : IsHovering ? Col.White.Override(a: 0.09f) : Theme.WidgetBackground).Value();
            canvas.DrawRoundRect(GetRect(), paint);
            float x = Position.X, y = Position.Y;
            using var titlePaint = new SKPaint { Typeface = Res.InterBold, TextSize = 13 };
            string title = Fit(row.Heading, titlePaint, 133);
            DrawText(canvas, title, x + 11, y + 17, 13, GetColor(Theme.TextMain), bold: true);
            float badgeX = x + 18 + titlePaint.MeasureText(title);
            using var badge = GetPaint(); badge.Color = GetColor(Theme.WidgetBackground).Value(); canvas.DrawRoundRect(SKRect.Create(badgeX, y + 6, 35, 16), 5, 5, badge);
            DrawText(canvas, "CCF " + row.Rank, badgeX + 17.5f, y + 14, 9, GetColor(Theme.TextSecond), centered: true, middle: true);
            string kind = row.Deadline.Kind == "abstract" ? "Abstract" : "Submission";
            string metadata = kind + " · " + row.Deadline.Utc.ToLocalTime().ToString("MMM dd, HH:mm", CultureInfo.InvariantCulture);
            DrawText(canvas, metadata, x + 11, y + 36, 10.5f, GetColor(Theme.TextSecond), maxWidth: 176);
            DrawText(canvas, Remaining(row.Deadline.Utc - DateTimeOffset.UtcNow), x + 276, y + 24, 14, GetColor(Theme.TextMain), right: true, middle: true, maxWidth: 83);
        }
    }
    private sealed class Label : UIObject
    {
        public string Text { get; set; } = "";
        public bool Bold { get; set; }
        public bool Center { get; set; }
        private readonly float _fontSize;
        private readonly Col _textColor;
        public Label(IslandObject island, float x, float y, float width, float height, float size, Col color) : base(island, new Vec2(x, y), new Vec2(width, height), UIAlignment.TopLeft)
        { Anchor = Vec2.zero; _fontSize = size; _textColor = color; }
        public override void Draw(SKCanvas canvas) => DrawText(canvas, Text, Position.X + (Center ? Size.X / 2 : 0), Position.Y + Size.Y / 2, _fontSize, GetColor(_textColor), Bold, centered: Center, middle: true, maxWidth: Size.X);
    }
    private sealed class DeadlinePanel : UIObject
    {
        public DeadlinePoint? Point { get; set; }
        public DeadlinePanel(IslandObject island, float x, float y, float width, float height) : base(island, new Vec2(x, y), new Vec2(width, height), UIAlignment.TopLeft) { Anchor = Vec2.zero; roundRadius = 11; }
        public override void Draw(SKCanvas canvas)
        {
            if (Point is not { } point) return;
            using var background = GetPaint(); background.Color = GetColor(Theme.WidgetBackground).Value(); canvas.DrawRoundRect(GetRect(), background);
            float x = Position.X + 12, y = Position.Y;
            DrawText(canvas, point.Kind == "abstract" ? "Abstract deadline" : "Submission deadline", x, y + 19, 11.5f, GetColor(Theme.TextMain), bold: true, maxWidth: 174);
            DrawText(canvas, Remaining(point.Utc - DateTimeOffset.UtcNow), Position.X + Size.X - 12, y + 19, 10.5f, GetColor(Theme.TextSecond), right: true);
            DrawText(canvas, "Local   " + point.Utc.ToLocalTime().ToString("MMM dd, HH:mm  zzz", CultureInfo.InvariantCulture), x, y + 40, 11, GetColor(Theme.TextSecond), maxWidth: Size.X - 24);
            DrawText(canvas, "Source   " + point.SourceTime + " " + point.Timezone, x, y + 59, 10.5f, GetColor(Theme.TextSecond), maxWidth: Size.X - 24);
        }
    }
    private static void DrawText(SKCanvas canvas, string value, float x, float y, float size, Col color, bool bold = false, bool centered = false, bool right = false, bool middle = false, float maxWidth = float.MaxValue)
    {
        using var paint = new SKPaint { IsAntialias = true, Typeface = bold ? Res.InterBold : Res.InterRegular, TextSize = size, Color = color.Value(), SubpixelText = false };
        string text = Fit(value ?? "", paint, maxWidth);
        float width = paint.MeasureText(text);
        if (centered) x -= width / 2; else if (right) x -= width;
        if (middle) y -= (paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2;
        canvas.DrawText(text, x, y, paint);
    }
    private static string Fit(string text, SKPaint paint, float width)
    {
        if (paint.MeasureText(text) <= width) return text;
        const string ellipsis = "…";
        int length = text.Length;
        while (length > 0 && paint.MeasureText(text[..length] + ellipsis) > width) length--;
        return length == 0 ? ellipsis : text[..length] + ellipsis;
    }
}
