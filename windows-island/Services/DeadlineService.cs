using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CcfDeadlineIsland.Models;
using YamlDotNet.Serialization;

namespace CcfDeadlineIsland.Services;

public sealed record DeadlineSnapshot(DateTimeOffset UpdatedAt, List<ConferenceEdition> Conferences);

public sealed class DeadlineService : IDisposable
{
    public static readonly string[] DataUrls = ["https://ccfddl.com/conference/allconf.yml", "https://ccfddl.github.io/conference/allconf.yml"];
    private readonly HttpClient _client;
    private readonly string _cachePath;
    public DeadlineService(string cachePath, HttpClient? client = null)
    {
        _cachePath = cachePath;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CCF-Deadline-Island/0.1.0");
    }

    public async Task<DeadlineSnapshot> FetchAsync(IEnumerable<string>? urls = null, CancellationToken cancellation = default)
    {
        var errors = new List<string>();
        foreach (string url in urls ?? DataUrls)
        {
            try
            {
                string yaml = await _client.GetStringAsync(url, cancellation).ConfigureAwait(false);
                var conferences = await Task.Run(() => Parse(yaml), cancellation).ConfigureAwait(false);
                return new DeadlineSnapshot(DateTimeOffset.UtcNow, conferences);
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
            {
                errors.Add($"{new Uri(url).Host}: {error.Message}");
            }
        }
        throw new HttpRequestException(string.Join("\n", errors));
    }

    public DeadlineSnapshot? ReadCache()
    {
        try
        {
            var cached = JsonSerializer.Deserialize<DeadlineSnapshot>(File.ReadAllText(_cachePath));
            return cached is not null && cached.Conferences is not null && cached.Conferences.All(c =>
                c is not null && !string.IsNullOrWhiteSpace(c.Id) && c.Deadlines is not null && c.Deadlines.All(d => d is not null)) ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    public bool SaveCache(DeadlineSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath + ".tmp", JsonSerializer.Serialize(snapshot));
            File.Move(_cachePath + ".tmp", _cachePath, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public static List<ConferenceEdition> Parse(string yaml)
    {
        var raw = new DeserializerBuilder().IgnoreUnmatchedProperties().Build().Deserialize<List<RawConference>>(yaml)
            ?? throw new InvalidDataException("会议数据为空");
        var result = new List<ConferenceEdition>();
        foreach (var conference in raw)
        {
            if (conference is null || string.IsNullOrWhiteSpace(conference.Title)) continue;
            foreach (var edition in conference.Editions ?? [])
            {
                if (edition is null || string.IsNullOrWhiteSpace(edition.Id)) continue;
                var points = new List<DeadlinePoint>();
                foreach (var timeline in edition.Timeline ?? [])
                {
                    if (timeline is null) continue;
                    string zone = timeline.Timezone ?? edition.Timezone ?? "UTC";
                    foreach (var (value, kind) in new[] { (timeline.Abstract, "abstract"), (timeline.Submission, "submission") })
                    {
                        if (value is not null && ParseTime(value, zone) is { } utc)
                            points.Add(new DeadlinePoint(kind, value, zone, utc, timeline.Comment));
                    }
                }
                if (points.Count == 0) continue;
                var rank = conference.Rank ?? new RawRank();
                result.Add(new ConferenceEdition(edition.Id, conference.Title, edition.Year, conference.Area ?? "",
                    rank.Ccf ?? "N", rank.Core ?? "N", rank.Thcpl ?? "N", edition.Website ?? "", points.OrderBy(d => d.Utc).ToList()));
            }
        }
        return result;
    }

    public static DateTimeOffset? ParseTime(string value, string timezone)
    {
        if (!DateTime.TryParseExact(value, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) return null;
        try
        {
            if (timezone == "AoE") return new DateTimeOffset(local, TimeSpan.FromHours(-12)).ToUniversalTime();
            if (timezone == "UTC") return new DateTimeOffset(local, TimeSpan.Zero);
            if (timezone.StartsWith("UTC+", StringComparison.Ordinal) || timezone.StartsWith("UTC-", StringComparison.Ordinal))
            {
                var parts = timezone[4..].Split(':');
                if (parts.Length > 2 || !int.TryParse(parts[0], out int hours) || hours > 14 || hours < 0) return null;
                int minutes = 0;
                if (parts.Length == 2 && (!int.TryParse(parts[1], out minutes) || minutes is < 0 or > 59)) return null;
                var offset = TimeSpan.FromMinutes((hours * 60 + minutes) * (timezone[3] == '-' ? -1 : 1));
                return new DateTimeOffset(local, offset).ToUniversalTime();
            }
            string zoneId = timezone == "PT" ? "America/Los_Angeles" : timezone;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out string? windowsId)) zoneId = windowsId;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            if (zone.IsInvalidTime(local)) return null;
            var utcOffset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            return new DateTimeOffset(local, utcOffset).ToUniversalTime();
        }
        catch (Exception error) when (error is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    public void Dispose() => _client.Dispose();
}
