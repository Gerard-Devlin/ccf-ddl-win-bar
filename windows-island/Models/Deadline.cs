using System.Globalization;
using YamlDotNet.Serialization;

namespace CcfDeadlineIsland.Models;

public sealed record DeadlinePoint(string Kind, string SourceTime, string Timezone, DateTimeOffset Utc, string? Comment)
{
    public string Label => Kind == "abstract" ? "摘要截止" : "投稿截止";
    public string Badge => Kind == "abstract" ? "A" : "S";
    public string LocalTime => Utc.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss  zzz");
    public string OriginalTime => $"{SourceTime}  {Timezone}";
    public string Heading => string.IsNullOrWhiteSpace(Comment) ? Label : Comment;
}

public sealed record ConferenceEdition(string Id, string Title, int Year, string Area, string Ccf, string Core,
    string Thcpl, string Website, List<DeadlinePoint> Deadlines)
{
    public DeadlinePoint? Next(DateTimeOffset now) => Deadlines.Where(d => d.Utc > now).MinBy(d => d.Utc);
    public string Heading => $"{Title} {Year}";
}

public sealed record DeadlineRow(ConferenceEdition Conference, DeadlinePoint Deadline, DateTimeOffset Now)
{
    public string Id => Conference.Id;
    public string Heading => Conference.Heading;
    public string Rank => Conference.Ccf;
    public string Kind => Deadline.Label;
    public string Metadata => $"{Deadline.Label}  ·  {Conference.Area}";
    public string Remaining => DeadlineFormatting.Remaining(Deadline.Utc - Now);
    public string Date => Deadline.Utc.ToLocalTime().ToString("MM/dd  HH:mm");
    public override string ToString() => $"{Heading} · {Kind} · {Remaining}";
}

public static class DeadlineFormatting
{
    public static string Remaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "已截止";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}天 {remaining.Hours}时";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}时 {remaining.Minutes}分";
        long seconds = (long)Math.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60}分 {seconds % 60}秒";
    }
}

public sealed class RawConference
{
    [YamlMember(Alias = "title")] public string Title { get; set; } = "";
    [YamlMember(Alias = "sub")] public string Area { get; set; } = "";
    [YamlMember(Alias = "rank")] public RawRank Rank { get; set; } = new();
    [YamlMember(Alias = "confs")] public List<RawEdition> Editions { get; set; } = [];
}
public sealed class RawRank
{
    [YamlMember(Alias = "ccf")] public string Ccf { get; set; } = "N";
    [YamlMember(Alias = "core")] public string Core { get; set; } = "N";
    [YamlMember(Alias = "thcpl")] public string Thcpl { get; set; } = "N";
}
public sealed class RawEdition
{
    [YamlMember(Alias = "id")] public string Id { get; set; } = "";
    [YamlMember(Alias = "year")] public int Year { get; set; }
    [YamlMember(Alias = "link")] public string Website { get; set; } = "";
    [YamlMember(Alias = "timezone")] public string Timezone { get; set; } = "UTC";
    [YamlMember(Alias = "timeline")] public List<RawTimeline> Timeline { get; set; } = [];
}
public sealed class RawTimeline
{
    [YamlMember(Alias = "abstract_deadline")] public string? Abstract { get; set; }
    [YamlMember(Alias = "deadline")] public string? Submission { get; set; }
    [YamlMember(Alias = "timezone")] public string? Timezone { get; set; }
    [YamlMember(Alias = "comment")] public string? Comment { get; set; }
}
