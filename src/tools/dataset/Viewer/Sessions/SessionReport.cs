using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>A session as the reports need it: its name, its log, and what it was recorded with.</summary>
internal sealed record ReportSession(string Name, IReadOnlyList<SessionFrame> Frames, SessionInfo? Info,
    ReplaySettings? Recorded, string RecordedSource);

/// <summary>
/// The Cards and Settings tabs of Analyse sessions as text: what the confidences of a session look like,
/// which cards were weak, and what other settings would have counted - per session, so sessions from
/// different devices can be put side by side. Plain text in a fixed-width font, which copies into a
/// note as it is.
/// </summary>
internal static class SessionReport
{
    private const int BarWidth = 24;

    public static string Settings(IReadOnlyList<ReportSession> sessions, ReplaySettings settings)
    {
        if (sessions.Count == 0) return "Open sessions to replay them.";

        var text = new StringBuilder();
        text.AppendLine($"Replayed with threshold {Number(settings.Threshold)}, {settings.RuleText}.");
        int appTotal = 0, replayTotal = 0, lostTotal = 0, addedTotal = 0;

        foreach (var session in sessions)
        {
            var app = session.Frames.Where(f => f.Committed && f.Card is not null).Select(f => f.Card!.Value).Distinct().ToList();
            var replay = SessionReplay.Count(session.Frames, settings).Select(c => c.Card).ToList();
            var lost = app.Except(replay).ToList();
            var added = replay.Except(app).ToList();
            appTotal += app.Count;
            replayTotal += replay.Count;
            lostTotal += lost.Count;
            addedTotal += added.Count;

            text.AppendLine();
            text.AppendLine(session.Name);
            Line(text, "", session.Info?.Describe() is { Length: > 0 } described
                ? described
                : "no session info beside the recording");
            if (session.Recorded is { } recorded)
            {
                Line(text, "recorded with", $"threshold {Number(recorded.Threshold)}, {recorded.RuleText}");
                Line(text, "", $"({session.RecordedSource})");
            }
            else
            {
                Line(text, "recorded with", "unknown: no rule explains the counts (a card removed by hand?)");
            }
            // A card counted that is not on the pile weighs far more than one missed: a user sees a missing
            // card, not a wrong one. So the additions come first.
            Line(text, "counted", $"{app.Count} in the app, {replay.Count} replayed");
            Line(text, "in addition", Cards(added));
            if (added.Count > 0) Line(text, "", "(not counted by the app: false counts, unless the app missed them)");
            Line(text, "no longer", Cards(lost));

            var brief = SessionReplay.ShortReadings(session.Frames, settings.Threshold);
            Line(text, "1-2 frames", $"{brief.Count} short readings" + (brief.Count == 0 ? "" :
                ", highest " + string.Join(" ", brief.OrderByDescending(r => r.Highest).Take(6).Select(r => Number(r.Highest)))));

            // Below the threshold the app was running with there is nothing in the log to replay.
            double floor = session.Recorded?.Threshold ?? session.Info?.ConfidenceThreshold ?? 0;
            if (settings.Threshold < floor - 1e-9)
                Line(text, "note", $"the log holds no detection below {Number(floor)}, so a lower threshold changes nothing");
            if (session.Frames.Any(f => f.Confidence > 1))
                Line(text, "note", "confidences above 1: Vision's sum over all classes, not the best class's score - not comparable with Android");

            text.AppendLine("  confidence");
            AppendHistogram(text, session.Frames);
        }

        if (sessions.Count > 1)
        {
            text.AppendLine();
            text.AppendLine($"All {sessions.Count} sessions: {appTotal} counted in the app, {replayTotal} replayed, " +
                            $"{lostTotal} no longer, {addedTotal} in addition.");
        }
        return text.ToString().TrimEnd();
    }

    public static string Cards(ReportSession session, ReplaySettings settings)
    {
        var replayed = SessionReplay.Count(session.Frames, settings).Select(c => c.Card).ToHashSet();
        var text = new StringBuilder();
        text.AppendLine(session.Name);
        text.AppendLine("Weakest first.");
        text.AppendLine($"at: frames at or above {Number(settings.Threshold)} · run: the longest run of them");
        text.AppendLine();
        text.AppendLine($"{"card",-15}{"frames",6}{"best",7}{"at",5}{"run",5}  {"app",-11}replay");
        foreach (var card in SessionReplay.Cards(session.Frames, settings.Threshold))
        {
            string app = card.CountedAt is { } at ? $"frame {at + 1}" : "-";
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{card.Card.Label,-15}{card.Frames,6}{card.Highest,7:0.000}{card.FramesAtThreshold,5}{card.LongestRunAtThreshold,5}  {app,-11}{(replayed.Contains(card.Card) ? "yes" : "NO")}"));
        }
        return text.ToString().TrimEnd();
    }

    private static void AppendHistogram(StringBuilder text, IReadOnlyList<SessionFrame> frames)
    {
        var counts = SessionReplay.Histogram(frames);
        int total = counts.Sum();
        var bands = SessionReplay.Bands;
        for (int i = 0; i < counts.Length; i++)
        {
            if (i == 0 && counts[0] == 0) continue;   // nothing below the app's own threshold is logged
            string band = i == counts.Length - 1 ? $"{Number(bands[i])} and up"
                : i == 0 ? $"below {Number(bands[1])}"
                : $"{Number(bands[i])}-{Number(bands[i + 1])}";
            int bar = total == 0 ? 0 : (int)Math.Round(counts[i] * (double)BarWidth / total);
            string share = total == 0 ? "" : $"{Math.Round(100.0 * counts[i] / total):0} %";
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    {band,-13}{new string('█', bar),-BarWidth} {counts[i],5} {share,5}"));
        }
    }

    /// <summary>
    /// One field of a session: its label in a column of its own, the value wrapped at word boundaries so the
    /// lines stay within the side panel and a continuation stays under the value.
    /// </summary>
    private static void Line(StringBuilder text, string label, string value)
    {
        const int width = 62;
        string indent = new(' ', 17);
        var line = new StringBuilder($"  {label,-15}");
        bool empty = true;
        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!empty && line.Length + 1 + word.Length > width)
            {
                text.AppendLine(line.ToString());
                line.Clear().Append(indent);
                empty = true;
            }
            if (!empty) line.Append(' ');
            line.Append(word);
            empty = false;
        }
        text.AppendLine(line.ToString());
    }

    private static string Cards(IReadOnlyCollection<JassCardEye.Dataset.Cards.Card> cards) =>
        cards.Count == 0 ? "-" : string.Join(", ", cards.Select(c => c.Label));

    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
