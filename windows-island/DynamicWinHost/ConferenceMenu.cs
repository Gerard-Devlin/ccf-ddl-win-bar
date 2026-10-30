// Content adaptation of Florian Butz's DynamicWin main UI.
// Source: 9f24d176a16333b1cfda21dd3ed48087497194ed, CC BY-SA 4.0.
// HomeMenu.cs / MediaWidget.cs geometry, fonts, primitives and button motion are retained.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CcfDeadlineIsland.Models;
using CcfDeadlineIsland.Services;
using DynamicWin.Main;
using DynamicWin.Resources;
using DynamicWin.UI;
using DynamicWin.UI.Menu.Menus;
using DynamicWin.UI.UIElements;
using DynamicWin.UI.Widgets;
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
    private readonly UIObject _topContainer;
    private readonly UIObject _listContainer;
    private readonly DWText _compactTitle;
    private readonly DWText _compactCountdown;
    private readonly DWTextImageButton _widgetButton;
    private readonly DWTextImageButton _listButton;
    private readonly ConferenceCard _card;
    private readonly List<UIObject> _expanded = new();
    private readonly List<UIObject> _listObjects = new();
    private readonly List<UIObject> _settingsObjects = new();
    private readonly List<DeadlineCard> _detailCards = new();
    private readonly List<ConferenceListRow> _listRows = new();
    private readonly DWText _listEmpty;
    private readonly DWText _listPage;
    private readonly DWText _detailPage;
    private readonly DWText _settingsStatus;
    private readonly DWTextButton[] _rankButtons;
    private readonly Checkbox _submissionOnly;
    private readonly Checkbox _animations;
    private int _listOffset;
    private int _detailOffset;
    private int _detailTotal;
    private string _detailConferenceId = "";
    private string _detailKey = "";

    public List<UIObject> UiObjects { get; } = new();
    public string Page { get; private set; } = "widgets";
    public Action<string>? PageChanged { get; set; }
    public DeadlineRow? Selected => _model.Rows.Count == 0 ? null : _model.Rows[Math.Clamp(_model.SelectedIndex, 0, _model.Rows.Count - 1)];

    public ConferenceMenu(IslandObject island, ConferenceModel model)
    {
        _island = island;
        _model = model;

        // HomeMenu's compact container and original 35px small-widget content inset.
        _compactContainer = new UIObject(island, Vec2.zero, IslandSize() - 35, UIAlignment.Center) { Color = Col.Transparent };
        _compactTitle = new DWText(_compactContainer, "CCF", Vec2.zero, UIAlignment.MiddleLeft)
        {
            Anchor = new Vec2(0, 0.5f), TextSize = 14, Font = Res.InterRegular, Color = Theme.TextMain
        };
        _compactCountdown = new DWText(_compactContainer, "--", Vec2.zero, UIAlignment.MiddleRight)
        {
            Anchor = new Vec2(1, 0.5f), TextSize = 14, Font = Res.InterRegular, Color = Theme.TextSecond
        };
        UiObjects.Add(_compactTitle);
        UiObjects.Add(_compactCountdown);

        // These dimensions/positions are HomeMenu's original Widgets / Tray / gear strip.
        _topContainer = new UIObject(island, new Vec2(0, 30), new Vec2(island.currSize.X, 30)) { Color = Col.Transparent };
        _widgetButton = MakeTab(Res.Widgets, "CCF", 42.5f, 75, 28.5f, () => ShowPage("widgets"));
        _listButton = MakeTab(Res.Tray, "List", 112.5f, 57.5f, 27.5f, () => ShowPage("list"));
        AddExpanded(_widgetButton);
        AddExpanded(_listButton);
        var settings = new DWImageButton(_topContainer, Res.Settings, new Vec2(-20, 0), new Vec2(20, 20), () => ShowPage("settings"), UIAlignment.MiddleRight)
        {
            normalColor = Col.Transparent, hoverColor = Col.Transparent,
            clickColor = Theme.Primary.Override(a: 0.35f), roundRadius = 25
        };
        AddExpanded(settings);

        _card = new ConferenceCard(island, model, () => SelectRelative(-1), () => ShowPage("details"), () => SelectRelative(1));
        AddExpanded(_card);

        // The source Tray slot is 275 x 250, with a 225px content region.
        _listContainer = new UIObject(island, new Vec2(0, -22.5f), new Vec2(225, 165), UIAlignment.BottomCenter)
        {
            Anchor = new Vec2(0.5f, 1), Color = Col.Transparent
        };
        for (int index = 0; index < 5; index++)
        {
            int slot = index;
            var row = new ConferenceListRow(_listContainer, new Vec2(0, 16 + index * 29), () =>
            {
                int selected = _listOffset + slot;
                if (selected >= _model.Rows.Count) return;
                _model.SelectedIndex = selected;
                ShowPage("details");
            });
            _listRows.Add(row);
            _listObjects.Add(row);
            AddExpanded(row);
        }
        _listEmpty = Text(_listContainer, "No matching conferences", new Vec2(0, 70), 12, Theme.TextSecond);
        _listPage = Text(island, "", new Vec2(0, -17), 10, Theme.TextThird, UIAlignment.BottomCenter);
        _listObjects.Add(_listEmpty); _listObjects.Add(_listPage);
        AddExpanded(_listEmpty); AddExpanded(_listPage);
        _detailPage = Text(island, "", new Vec2(0, -17), 10, Theme.TextThird, UIAlignment.BottomCenter);
        AddExpanded(_detailPage);

        var title = Text(island, "CCF filters", new Vec2(25, 67), 24, Theme.TextMain, UIAlignment.TopLeft, true);
        title.Anchor.X = 0;
        AddSetting(title);
        AddSetting(LeftLabel("CCF rank", 107));
        _rankButtons = Preferences.CcfValues.Select((rank, index) =>
        {
            var button = new DWTextButton(island, rank == "N" ? "Unranked" : rank,
                new Vec2(25 + index * 90, 137), new Vec2(75, 25), () =>
                {
                    var selected = _model.Preferences.Ccf;
                    if (selected.Contains(rank)) selected.Remove(rank); else selected.Add(rank);
                    _model.SavePreferences?.Invoke();
                }, UIAlignment.TopLeft) { Anchor = new Vec2(0, 0.5f), normalTextSize = 14 };
            button.Text.Color = Theme.TextSecond;
            AddSetting(button);
            return button;
        }).ToArray();

        AddSetting(LeftLabel("Deadline range", 177));
        int[] ranges = { 30, 90, 180, 365 };
        var range = new MultiSelectionButton(island, ranges.Select(days => days + " days").ToArray(),
            new Vec2(25, 207), new Vec2(475, 25), UIAlignment.TopLeft) { Anchor = new Vec2(0, 0.5f) };
        range.SelectedIndex = Math.Max(0, Array.IndexOf(ranges, model.Preferences.WithinDays));
        range.onClick += index => { _model.Preferences.WithinDays = ranges[index]; _model.SavePreferences?.Invoke(); };
        AddSetting(range);

        _submissionOnly = new Checkbox(island, "Submission deadlines only", new Vec2(25, 252), new Vec2(25, 25), () =>
        {
            _model.Preferences.SubmissionOnly = _submissionOnly!.IsChecked;
            _model.SavePreferences?.Invoke();
        }, UIAlignment.TopLeft) { Anchor = new Vec2(0, 0.5f), IsChecked = model.Preferences.SubmissionOnly };
        AddSetting(_submissionOnly);
        _animations = new Checkbox(island, "Toggle animations", new Vec2(25, 290), new Vec2(25, 25), () =>
        {
            _model.Preferences.ReducedMotion = !_animations!.IsChecked;
            _model.SavePreferences?.Invoke();
        }, UIAlignment.TopLeft) { Anchor = new Vec2(0, 0.5f), IsChecked = !model.Preferences.ReducedMotion };
        AddSetting(_animations);
        var refresh = new DWTextButton(island, "Refresh conference data", new Vec2(25, 336), new Vec2(225, 25), () => _model.Refresh?.Invoke(), UIAlignment.TopLeft)
        {
            Anchor = new Vec2(0, 0.5f), normalTextSize = 14
        };
        AddSetting(refresh);
        _settingsStatus = Text(island, "", new Vec2(25, 356), 11, Theme.TextThird, UIAlignment.TopLeft);
        _settingsStatus.Anchor.X = 0;
        AddSetting(_settingsStatus);
        var back = new DWTextButton(island, "Save changes", new Vec2(0, -45), new Vec2(250, 40), () =>
        {
            _model.SavePreferences?.Invoke(); ShowPage("widgets");
        }, UIAlignment.BottomCenter) { roundRadius = 25 };
        back.Text.Font = Res.InterBold;
        AddSetting(back);

        foreach (var item in _expanded) item.SilentSetActive(false);
    }

    public Vec2 IslandSize()
    {
        // HomeMenu grows compact width from its two small-widget widths and 57.5px spacing.
        var selected = Selected;
        string title = selected is null ? "CCF" : DWText.Truncate(selected.Conference.Title, 12);
        string countdown = selected is null ? (_model.Loading ? "Loading" : "--") : Remaining(selected.Deadline.Utc - DateTimeOffset.UtcNow);
        using var paint = new SKPaint { Typeface = Res.InterRegular, TextSize = 14 };
        return new Vec2(Math.Max(200, paint.MeasureText(title) + paint.MeasureText(countdown) + 57.5f), 35);
    }
    public Vec2 IslandSizeBig()
    {
        if (Page == "settings") return new Vec2(530, 430);
        if (Page == "list") return new Vec2(275, 250);
        if (Page == "details" && _detailCards.Count > 1)
            return new Vec2(460, _detailCards.Count > 2 ? 310 : 195);
        return new Vec2(275, 180);
    }
    public Col IslandBorderColor() => Col.Transparent;

    public void ShowPage(string page)
    {
        if (page is not ("widgets" or "list" or "details" or "settings")) page = "widgets";
        if (page == "details" && Selected is null) page = "widgets";
        Page = page;
        if (page == "details") BuildDetails();
        PageChanged?.Invoke(page);
    }

    public void Scroll(int delta)
    {
        if (delta == 0) return;
        if (Page == "list") _listOffset = Math.Clamp(_listOffset + (delta > 0 ? -1 : 1), 0, Math.Max(0, _model.Rows.Count - 5));
        else if (Page == "details")
        {
            BuildDetails();
            _detailOffset = Math.Clamp(_detailOffset + (delta > 0 ? -2 : 2), 0, Math.Max(0, _detailTotal - 4));
            BuildDetails();
        }
        else if (Page == "widgets") SelectRelative(delta > 0 ? -1 : 1);
    }

    public void SelectRelative(int delta)
    {
        if (_model.Rows.Count == 0) return;
        _model.SelectedIndex = ((_model.SelectedIndex + delta) % _model.Rows.Count + _model.Rows.Count) % _model.Rows.Count;
        if (Page == "details") BuildDetails();
    }

    public void Update()
    {
        bool expanded = _island.IsHovering;
        _model.SelectedIndex = Math.Clamp(_model.SelectedIndex, 0, Math.Max(0, _model.Rows.Count - 1));
        var selected = Selected;
        _compactContainer.Size = IslandSize() - 35;
        _compactTitle.SilentSetText(selected is null ? "CCF" : DWText.Truncate(selected.Conference.Title, 12));
        _compactCountdown.SilentSetText(selected is null ? (_model.Loading ? "Loading" : "--") : Remaining(selected.Deadline.Utc - DateTimeOffset.UtcNow));
        _compactTitle.SetActive(!expanded);
        _compactCountdown.SetActive(!expanded);

        _topContainer.Size = new Vec2(_island.currSize.X - 30, 30);
        _widgetButton.SetActive(expanded);
        _listButton.SetActive(expanded);
        _expanded[2].SetActive(expanded); // Original gear button.
        _card.SetActive(expanded && Page == "widgets");
        _card.Refresh(selected, _model.Loading);
        Col tabActive = Col.White.Override(a: 0.075f);
        float smoothing = Math.Clamp(15 * RendererMain.Instance.DeltaTime, 0, 1);
        _widgetButton.normalColor = Col.Lerp(_widgetButton.normalColor, Page == "widgets" ? tabActive : Col.Transparent, smoothing);
        _widgetButton.hoverColor = _widgetButton.normalColor;
        _listButton.normalColor = Col.Lerp(_listButton.normalColor, Page == "list" ? tabActive : Col.Transparent, smoothing);
        _listButton.hoverColor = _listButton.normalColor;

        _listOffset = Math.Clamp(_listOffset, 0, Math.Max(0, _model.Rows.Count - 5));
        for (int index = 0; index < _listRows.Count; index++)
        {
            var row = _model.Rows.ElementAtOrDefault(_listOffset + index);
            _listRows[index].Refresh(row);
            _listRows[index].SetActive(expanded && Page == "list" && row is not null);
        }
        _listEmpty.SetActive(expanded && Page == "list" && _model.Rows.Count == 0);
        _listPage.SetActive(expanded && Page == "list" && _model.Rows.Count > 0);
        _listPage.SilentSetText($"{_listOffset + 1}-{Math.Min(_model.Rows.Count, _listOffset + 5)} / {_model.Rows.Count}");

        if (Page == "details") BuildDetails();
        foreach (var card in _detailCards) card.SetActive(expanded && Page == "details");
        _detailPage.SetActive(expanded && Page == "details" && _detailTotal > 4);
        foreach (var item in _settingsObjects) item.SetActive(expanded && Page == "settings");
        for (int index = 0; index < _rankButtons.Length; index++)
            _rankButtons[index].normalColor = _model.Preferences.Ccf.Contains(Preferences.CcfValues[index])
                ? (Theme.Primary * 0.65f).Override(a: 0.75f) : Theme.Secondary.Override(a: 0.9f);
        _settingsStatus.SilentSetText(DWText.Truncate(_model.Status, 68));
    }

    public static string Remaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "Expired";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        long seconds = (long)Math.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60}m {seconds % 60}s";
    }

    private DWTextImageButton MakeTab(SKBitmap icon, string label, float x, float width, float textX, Action click)
    {
        var button = new DWTextImageButton(_topContainer, icon, label, new Vec2(x, 0), new Vec2(width, 20), click, UIAlignment.MiddleLeft)
        {
            normalColor = Col.Transparent, hoverColor = Col.Transparent,
            clickColor = Theme.Primary.Override(a: 0.35f), roundRadius = 25
        };
        button.Text.alignment = UIAlignment.MiddleLeft;
        button.Text.Anchor.X = 0;
        button.Text.Position = new Vec2(textX, 0);
        return button;
    }

    private void AddExpanded(UIObject item) { _expanded.Add(item); UiObjects.Add(item); }
    private void AddSetting(UIObject item) { _settingsObjects.Add(item); AddExpanded(item); }
    private DWText LeftLabel(string text, float y)
    {
        var label = Text(_island, text, new Vec2(25, y), 15, Theme.TextSecond, UIAlignment.TopLeft);
        label.Anchor.X = 0;
        return label;
    }
    private static DWText Text(UIObject parent, string value, Vec2 position, float size, Col color,
        UIAlignment alignment = UIAlignment.TopCenter, bool bold = false) => new(parent, value, position, alignment)
        { TextSize = size, Font = bold ? Res.InterBold : Res.InterRegular, Color = color };

    private void BuildDetails()
    {
        var selected = Selected;
        if (selected is null)
        {
            _detailTotal = 0;
            _detailOffset = 0;
            _detailConferenceId = "";
            _detailKey = "";
            foreach (var card in _detailCards) { card.DestroyCall(); UiObjects.Remove(card); _expanded.Remove(card); }
            _detailCards.Clear();
            _detailPage.SilentSetText("");
            return;
        }
        if (_detailConferenceId != selected.Id)
        {
            _detailConferenceId = selected.Id;
            _detailOffset = 0;
        }
        var allPoints = selected.Conference.Deadlines.Where(point => point.Utc > DateTimeOffset.UtcNow).OrderBy(point => point.Utc).ToList();
        _detailTotal = allPoints.Count;
        _detailOffset = Math.Clamp(_detailOffset, 0, Math.Max(0, _detailTotal - 4));
        var points = allPoints.Skip(_detailOffset).Take(4).ToList();
        _detailPage.SilentSetText(_detailTotal > 4 ? $"{_detailOffset + 1}-{Math.Min(_detailTotal, _detailOffset + 4)} / {_detailTotal}" : "");
        string key = $"{selected.Id}:{_detailOffset}:{_detailTotal}:" + string.Join("|", points.Select(point => $"{point.Kind}:{point.Utc.Ticks}:{point.SourceTime}:{point.Timezone}"));
        if (key == _detailKey) return;
        _detailKey = key;
        foreach (var card in _detailCards) { card.DestroyCall(); UiObjects.Remove(card); _expanded.Remove(card); }
        _detailCards.Clear();
        for (int index = 0; index < points.Count; index++)
        {
            int column = points.Count == 1 ? 0 : index % 2;
            int line = points.Count <= 2 ? 0 : index / 2;
            float x = points.Count == 1 ? 0 : column == 0 ? -107.5f : 107.5f;
            float y = points.Count <= 2 ? -25 : -25 - (1 - line) * 115;
            var card = new DeadlineCard(_island, points[index], new Vec2(x, y), _model);
            card.SilentSetActive(false);
            _detailCards.Add(card);
            AddExpanded(card);
        }
    }

    private sealed class ConferenceCard : WidgetBase
    {
        private readonly ConferenceModel _model;
        private readonly DWText _title;
        private readonly DWText _subtitle;
        private System.Windows.Controls.ContextMenu? _contextMenu;
        public ConferenceCard(IslandObject island, ConferenceModel model, Action previous, Action details, Action next)
            : base(island, new Vec2(0, -25), UIAlignment.BottomCenter)
        {
            _model = model;
            Anchor = new Vec2(0.5f, 1);
            // MediaWidget's exact title, subtitle and control slots.
            _title = new DWText(this, "CCF Deadlines", new Vec2(0, 22.5f)) { Color = Theme.TextSecond, Font = Res.InterBold, TextSize = 15 };
            _subtitle = new DWText(this, "Loading...", new Vec2(0, 42.5f)) { Color = Theme.TextThird, Font = Res.InterRegular, TextSize = 13 };
            AddLocalObject(_title); AddLocalObject(_subtitle);
            AddLocalObject(Control(Res.Previous, -50, previous, 0.65f));
            AddLocalObject(Control(Res.FileIcon, 0, details, 0.8f));
            AddLocalObject(Control(Res.Next, 50, next, 0.65f));
        }
        private DWImageButton Control(SKBitmap icon, float x, Action action, float imageScale) =>
            new(this, icon, new Vec2(x, 25), new Vec2(30, 30), action, UIAlignment.Center)
            {
                roundRadius = 25, normalColor = Col.Transparent,
                hoverColor = Col.White.Override(a: 0.1f), clickColor = Col.White.Override(a: 0.25f),
                hoverScaleMulti = Vec2.one * 1.25f, imageScale = imageScale
            };
        public void Refresh(DeadlineRow? row, bool loading)
        {
            _title.Text = row is null ? (loading ? "Loading..." : "No conferences") : DWText.Truncate(row.Heading, 20);
            _subtitle.SilentSetText(row is null ? "CCF Deadlines" : $"{(row.Deadline.Kind == "abstract" ? "Abstract" : "Submission")} · {Remaining(row.Deadline.Utc - DateTimeOffset.UtcNow)}");
        }
        public override void DrawWidget(SKCanvas canvas)
        {
            using var paint = GetPaint();
            paint.Color = GetColor(Theme.WidgetBackground).Value();
            canvas.DrawRoundRect(GetRect(), paint);
        }
        public override System.Windows.Controls.ContextMenu? GetContextMenu() => _contextMenu ??= MakeContext(_model);
    }

    private sealed class ConferenceListRow : DWButton
    {
        private readonly DWText _title;
        private readonly DWText _countdown;
        public ConferenceListRow(UIObject parent, Vec2 position, Action click)
            : base(parent, position, new Vec2(225, 28), click)
        {
            normalColor = Col.Transparent; hoverColor = Theme.WidgetBackground; clickColor = Theme.Primary.Override(a: 0.35f); roundRadius = 15;
            _title = new DWText(this, "", new Vec2(9, 0), UIAlignment.MiddleLeft) { Anchor = new Vec2(0, 0.5f), Font = Res.InterBold, TextSize = 11, Color = Theme.TextSecond };
            _countdown = new DWText(this, "", new Vec2(-9, 0), UIAlignment.MiddleRight) { Anchor = new Vec2(1, 0.5f), Font = Res.InterRegular, TextSize = 10, Color = Theme.TextThird };
            AddLocalObject(_title); AddLocalObject(_countdown);
        }
        public void Refresh(DeadlineRow? row)
        {
            _title.SilentSetText(row is null ? "" : DWText.Truncate(row.Heading, 17));
            _countdown.SilentSetText(row is null ? "" : Remaining(row.Deadline.Utc - DateTimeOffset.UtcNow));
        }
    }

    private sealed class DeadlineCard : WidgetBase
    {
        private readonly ConferenceModel _model;
        private readonly DeadlinePoint _point;
        private readonly DWText _remaining;
        private System.Windows.Controls.ContextMenu? _contextMenu;
        public DeadlineCard(IslandObject island, DeadlinePoint point, Vec2 position, ConferenceModel model)
            : base(island, position, UIAlignment.BottomCenter)
        {
            _model = model;
            _point = point;
            Anchor = new Vec2(0.5f, 1);
            var heading = Text(this, point.Kind == "abstract" ? "Abstract deadline" : "Submission deadline", new Vec2(15, 20), 13, Theme.TextSecond, UIAlignment.TopLeft, true);
            heading.Anchor.X = 0;
            AddLocalObject(heading);
            AddLocalObject(LeftTime(point.Utc.ToLocalTime().ToString("MM/dd  HH:mm  zzz", CultureInfo.InvariantCulture), 43));
            AddLocalObject(LeftTime(DWText.Truncate(point.SourceTime + " " + point.Timezone, 27), 63));
            _remaining = Text(this, Remaining(point.Utc - DateTimeOffset.UtcNow), new Vec2(15, -16), 12, Theme.TextMain, UIAlignment.BottomLeft);
            _remaining.Anchor.X = 0;
            AddLocalObject(_remaining);
        }
        private DWText LeftTime(string value, float y)
        {
            var text = Text(this, value, new Vec2(15, y), 11, Theme.TextThird, UIAlignment.TopLeft);
            text.Anchor.X = 0;
            return text;
        }
        public override void DrawWidget(SKCanvas canvas)
        {
            using var paint = GetPaint(); paint.Color = GetColor(Theme.WidgetBackground).Value();
            canvas.DrawRoundRect(GetRect(), paint);
        }
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _remaining.SilentSetText(Remaining(_point.Utc - DateTimeOffset.UtcNow));
        }
        public override System.Windows.Controls.ContextMenu? GetContextMenu() => _contextMenu ??= MakeContext(_model);
    }

    private static System.Windows.Controls.ContextMenu MakeContext(ConferenceModel model)
    {
        var context = new System.Windows.Controls.ContextMenu();
        foreach (var (label, action) in new (string, Action?)[]
        {
            ("Open conference website", model.OpenWebsite), ("Copy deadline times", model.CopyTime),
            ("Export calendar (.ics)", model.ExportCalendar), ("Refresh", model.Refresh)
        })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, IsEnabled = action is not null };
            item.Click += (_, _) => action?.Invoke();
            context.Items.Add(item);
        }
        return context;
    }
}
