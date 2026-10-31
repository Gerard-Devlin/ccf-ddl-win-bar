using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using CcfDeadlineIsland.Models;
using CcfDeadlineIsland.Services;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}"); passed++;
}
var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
var yaml = """
- title: TEST
  sub: AI
  rank: { ccf: A, core: A*, thcpl: A }
  ignored_field: anything
  confs:
    - id: test30
      year: 2030
      link: https://example.com/test
      timezone: UTC
      timeline:
        - abstract_deadline: '2030-01-01 00:00:01'
          deadline: '2030-01-02 00:00:00'
          comment: Main track
        - deadline: TBD
""";
var editions = DeadlineService.Parse(yaml);
Check(editions.Count == 1 && editions[0].Deadlines.Count == 2, "YAML parses full timeline and ignores unknown fields / TBD");
Check(DeadlineService.ParseTime("2030-01-01 23:59:00", "AoE") == DateTimeOffset.Parse("2030-01-02T11:59:00Z"), "AoE timezone");
Check(DeadlineService.ParseTime("2030-01-01 12:00:00", "UTC+5:30") == DateTimeOffset.Parse("2030-01-01T06:30:00Z"), "Fractional UTC offset");
Check(DeadlineService.ParseTime("2030-07-01 12:00:00", "PT") == DateTimeOffset.Parse("2030-07-01T19:00:00Z"), "Pacific daylight saving time");
Check(DeadlineService.ParseTime("2030-01-01 12:00:00", "UTC+8:80") is null && DeadlineService.ParseTime("TBD", "UTC") is null, "Invalid time / offsets are skipped");
var preferences = new Preferences();
Check(preferences.Visible(editions, now).Single().Deadline.Kind == "abstract", "Nearest future abstract is selected");
Check(preferences.Visible(editions, now.AddSeconds(1)).Single().Deadline.Kind == "submission", "Expiry advances to submission at the exact boundary");
Check(preferences.Visible(editions, now.AddDays(1)).Count == 0, "Fully expired conference is removed");
preferences.SubmissionOnly = true;
Check(preferences.Visible(editions, now).Single().Deadline.Kind == "submission", "Submission-only preference");
preferences.Ccf.Clear();
Check(preferences.Visible(editions, now).Count == 0, "Cleared rank group matches no conferences");
preferences = new Preferences { Search = "test" };
var later = editions[0] with { Id = "later30", Title = "LATER", Deadlines = [new("submission", "", "UTC", now.AddDays(3), null)] };
Check(preferences.Visible([later, .. editions, .. editions], now).Count == 1, "Search is case-insensitive and editions are deduplicated");
preferences.Search = "";
Check(preferences.Visible([later, .. editions], now).First().Id == "test30", "Deadline ordering");
var allPreferences = new Preferences { ItemCount = 3 };
var allEditions = Enumerable.Range(0, 28).Select(index => later with { Id = "all-" + index });
Check(allPreferences.Visible(allEditions, now).Count == 28, "All conferences ignores the legacy short-list limit");
Check(DeadlineFormatting.Remaining(TimeSpan.FromDays(12) + TimeSpan.FromHours(22)) == "12天 22时", "Long countdown omits seconds");
Check(!DeadlineFormatting.Remaining(TimeSpan.FromMilliseconds(400)).Contains("0秒"), "Positive subsecond deadline is not zero");
Check(DeadlineFormatting.Remaining(TimeSpan.FromMinutes(1)) == "1分 0秒"
    && DeadlineFormatting.Remaining(TimeSpan.FromMilliseconds(60400)) == "1分 1秒", "Short countdown rounds up without adding a second at exact minute boundaries");

string temp = Path.Combine(Path.GetTempPath(), "CcfIslandTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    using var service = new DeadlineService(Path.Combine(temp, "cache.json"));
    var snapshot = new DeadlineSnapshot(now, editions);
    Check(service.SaveCache(snapshot) && service.ReadCache()?.Conferences.Single().Id == "test30", "Cache round trip");
    File.WriteAllText(Path.Combine(temp, "cache.json"), "{broken");
    Check(service.ReadCache() is null, "Corrupt cache is ignored");
    File.WriteAllText(Path.Combine(temp, "cache.json"), "{\"UpdatedAt\":\"2030-01-01T00:00:00Z\",\"Conferences\":[null]}");
    Check(service.ReadCache() is null, "Invalid cached record is ignored");
    File.WriteAllText(Path.Combine(temp, "not-a-directory"), "x");
    using var blocked = new DeadlineService(Path.Combine(temp, "not-a-directory", "cache.json"));
    Check(!blocked.SaveCache(snapshot), "Storage failure is nonfatal");
    preferences.Save(Path.Combine(temp, "prefs.json"));
    Check(Preferences.Load(Path.Combine(temp, "prefs.json")).WithinDays == preferences.WithinDays, "Preferences round trip");
    preferences.DockSide = "right"; preferences.DockPosition = .73;
    preferences.Save(Path.Combine(temp, "dock-prefs.json"));
    var dockPreferences = Preferences.Load(Path.Combine(temp, "dock-prefs.json"));
    Check(dockPreferences.DockSide == "right" && dockPreferences.DockPosition == .73, "Dock edge and vertical position survive restart");
    var handler = new FixtureHandler("not: [yaml", yaml);
    using var network = new DeadlineService(Path.Combine(temp, "network.json"), new HttpClient(handler));
    var fetched = await network.FetchAsync(["https://primary.test/data", "https://mirror.test/data"]);
    Check(handler.Requests == 2 && fetched.Conferences.Single().Id == "test30", "Invalid primary YAML falls back to mirror");
    var failedHandler = new FixtureHandler(null, null);
    using var offline = new DeadlineService(Path.Combine(temp, "offline.json"), new HttpClient(failedHandler));
    offline.SaveCache(snapshot);
    try { await offline.FetchAsync(["https://primary.test/data", "https://mirror.test/data"]); throw new Exception("Expected failure"); }
    catch (HttpRequestException error)
    {
        Check(error.Message.Contains("primary.test") && error.Message.Contains("mirror.test") && offline.ReadCache()?.Conferences.Count == 1, "Offline refresh retains cache and reports both source errors");
    }
}
finally { Directory.Delete(temp, true); }

var longTitle = editions[0] with { Title = string.Concat(Enumerable.Repeat("会议,分号;换行\n", 30)) };
string calendar = CalendarExport.Create(longTitle, now);
Check(calendar.Split("BEGIN:VEVENT").Length == 3 && calendar.Contains("DTSTART:20300101T000001Z"), "Calendar contains abstract and submission as UTC events");
Check(calendar.Contains("\\,") && calendar.Contains("\\;") && calendar.Contains("\\n"), "Calendar escapes reserved characters");
Check(calendar.Split("\r\n").All(line => Encoding.UTF8.GetByteCount(line) <= 75), "Calendar lines fold at 75 UTF-8 octets");
var sameTimeCycles = editions[0] with { Deadlines = [editions[0].Deadlines[1], editions[0].Deadlines[1], editions[0].Deadlines[1] with { Comment = "Different track" }] };
string cycleCalendar = CalendarExport.Create(sameTimeCycles, now);
var cycleUids = cycleCalendar.Split("\r\n").Where(line => line.StartsWith("UID:", StringComparison.Ordinal)).ToList();
Check(cycleUids.Count == 2 && cycleUids.Distinct(StringComparer.Ordinal).Count() == 2, "Calendar deduplicates repeated points and gives concurrent tracks distinct event UIDs");
var punctuationUrl = editions[0] with { Website = "https://example.com/track;round?areas=ai,db" };
Check(CalendarExport.Create(punctuationUrl, now).Contains("URL:https://example.com/track;round?areas=ai,db\r\n"), "Calendar preserves URI punctuation in URL properties");
PlacementChecks.Run(Check);
DockingChecks.Run(Check);
if (args.Contains("--live", StringComparer.Ordinal))
{
    using var live = new DeadlineService(Path.Combine(Path.GetTempPath(), "CcfIslandLiveCheck.json"));
    var snapshot = await live.FetchAsync();
    Check(snapshot.Conferences.Count > 0, "Live CCF YAML parses into conference editions");
    var liveNow = DateTimeOffset.UtcNow;
    var visible = new Preferences().Visible(snapshot.Conferences, liveNow);
    Check(visible.Count > 0 && visible.All(row => row.Deadline.Utc > liveNow), "Live upcoming list contains only future deadlines");
    Check(visible.Zip(visible.Skip(1), (left, right) => left.Deadline.Utc <= right.Deadline.Utc).All(ordered => ordered), "Live upcoming list remains chronologically sorted");
    Console.WriteLine($"Live dataset: {snapshot.Conferences.Count} editions, {visible.Count} visible upcoming conferences.");
}
Console.WriteLine($"{passed} checks passed.");

sealed class FixtureHandler(params string?[] responses) : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = responses[Requests++];
        return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(body ?? "Unavailable") });
    }
}
