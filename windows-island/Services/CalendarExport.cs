using System.Text;
using System.Security.Cryptography;
using CcfDeadlineIsland.Models;

namespace CcfDeadlineIsland.Services;

public static class CalendarExport
{
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(",", "\\,").Replace(";", "\\;");
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
    public static string Create(ConferenceEdition conference, DateTimeOffset now)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//CCF Deadline Island//CCF Deadlines//ZH", "CALSCALE:GREGORIAN", "METHOD:PUBLISH" };
        foreach (var point in conference.Deadlines.Where(d => d.Utc > now)
            .DistinctBy(d => (d.Kind, d.Utc, d.Comment)).OrderBy(d => d.Utc))
        {
            string cycleId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(point.Comment ?? "")))[..12];
            lines.AddRange(["BEGIN:VEVENT", $"UID:{Uri.EscapeDataString(conference.Id)}-{point.Kind}-{point.Utc.ToUnixTimeSeconds()}-{cycleId}@ccfddl-island",
                $"DTSTAMP:{Stamp(now)}", $"DTSTART:{Stamp(point.Utc)}", $"DTEND:{Stamp(point.Utc.AddMinutes(1))}",
                $"SUMMARY:{Escape(conference.Heading + " · " + point.Label)}", $"DESCRIPTION:{Escape(point.OriginalTime + "\n" + point.Comment)}"]);
            if (Uri.TryCreate(conference.Website, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") lines.Add($"URL:{uri.AbsoluteUri}");
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }
    // RFC 5545 folds at 75 UTF-8 octets without splitting a Unicode scalar.
    private static string Fold(string line)
    {
        var result = new StringBuilder();
        int bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            int size = rune.Utf8SequenceLength;
            if (bytes + size > 75) { result.Append("\r\n "); bytes = 1; }
            result.Append(rune.ToString()); bytes += size;
        }
        return result.ToString();
    }
}
