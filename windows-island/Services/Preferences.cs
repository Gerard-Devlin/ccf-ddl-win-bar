using System.IO;
using System.Text.Json;
using CcfDeadlineIsland.Models;

namespace CcfDeadlineIsland.Services;

public sealed class Preferences
{
    public static readonly string[] CcfValues = ["A", "B", "C", "N"];
    public static readonly string[] CoreValues = ["A*", "A", "B", "C", "N"];
    public static readonly string[] ThcplValues = ["A", "B", "N"];
    public static readonly Dictionary<string, string> Areas = new()
    {
        ["AI"] = "人工智能", ["DB"] = "数据库", ["SE"] = "软件工程", ["SC"] = "网络安全",
        ["CG"] = "图形与多媒体", ["DS"] = "体系结构与存储", ["NW"] = "计算机网络",
        ["CT"] = "计算理论", ["HI"] = "人机交互", ["MX"] = "交叉学科"
    };
    public List<string> Ccf { get; set; } = [.. CcfValues];
    public List<string> Core { get; set; } = [.. CoreValues];
    public List<string> Thcpl { get; set; } = [.. ThcplValues];
    public List<string> Categories { get; set; } = [.. Areas.Keys];
    public int WithinDays { get; set; } = 365;
    public int ItemCount { get; set; } = 20;
    public bool SubmissionOnly { get; set; }
    public int Monitor { get; set; }
    public int TopOffset { get; set; } = 12;
    public string DockSide { get; set; } = "top";
    public double DockPosition { get; set; } = .5;
    public string Search { get; set; } = "";

    public List<DeadlineRow> Visible(IEnumerable<ConferenceEdition> conferences, DateTimeOffset now)
    {
        return conferences.Select(conference => new { Conference = conference, Deadline = conference.Deadlines
                .Where(d => d.Utc > now && (!SubmissionOnly || d.Kind == "submission")).MinBy(d => d.Utc) })
            .Where(item => item.Deadline is not null && item.Deadline.Utc <= now.AddDays(WithinDays)
                && Ccf.Contains(item.Conference.Ccf) && Core.Contains(item.Conference.Core) && Thcpl.Contains(item.Conference.Thcpl)
                && Categories.Contains(item.Conference.Area) && (string.IsNullOrWhiteSpace(Search)
                    || item.Conference.Heading.Contains(Search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Deadline!.Utc).DistinctBy(item => item.Conference.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => new DeadlineRow(item.Conference, item.Deadline!, now)).ToList();
    }

    public static Preferences Load(string path)
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
            if (preferences is null || preferences.Ccf is null || preferences.Core is null || preferences.Thcpl is null || preferences.Categories is null) return new();
            preferences.WithinDays = new[] { 30, 90, 180, 365 }.Contains(preferences.WithinDays) ? preferences.WithinDays : 365;
            preferences.ItemCount = Math.Clamp(preferences.ItemCount, 3, 50);
            preferences.TopOffset = Math.Clamp(preferences.TopOffset, 0, 100);
            preferences.DockSide = Placement.DockPlacement.NormalizeSide(preferences.DockSide);
            preferences.DockPosition = Placement.DockPlacement.NormalizePosition(preferences.DockPosition);
            preferences.Search = "";
            return preferences;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public bool Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
            File.Move(path + ".tmp", path, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
