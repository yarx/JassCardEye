using System;
using System.Collections.Generic;
using System.Linq;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Viewer.Sessions;

internal enum AnomalyKind { Outlier, Ghost, FlipFlop, CommitMismatch }
internal sealed record SessionAnomaly(int Frame, int EndFrame, AnomalyKind Kind, Card Observed, Card? Expected);
internal sealed record SessionRun(int Start, int End, Card? Card)
{
    public int Count => End - Start + 1;
}
internal sealed record PairCount(Card First, Card Second, int Count);
internal sealed record CardCount(Card Card, int Count);
internal sealed record SessionTotals(IReadOnlyList<PairCount> Confusions, IReadOnlyList<CardCount> Ghosts,
    IReadOnlyList<PairCount> Switches);

/// <summary>Heuristic candidates, not ground truth. Runs and neighbourhoods are measured in recorded
/// frames; each suspicious event is counted once even when two heuristics flag the same frame.</summary>
internal sealed class SessionAnalysis
{
    public IReadOnlyList<SessionFrame> Frames { get; }
    public IReadOnlyList<SessionRun> Runs { get; }
    public IReadOnlyList<SessionAnomaly> Anomalies { get; }

    public SessionAnalysis(IReadOnlyList<SessionFrame> frames)
    {
        Frames = frames;
        var runs = new List<SessionRun>();
        for (var start = 0; start < frames.Count;)
        {
            var end = start;
            while (end + 1 < frames.Count && frames[end + 1].Card == frames[start].Card) end++;
            runs.Add(new(start, end, frames[start].Card));
            start = end + 1;
        }
        Runs = runs;
        var anomalies = new List<SessionAnomaly>();
        for (var r = 1; r + 1 < runs.Count; r++)
        {
            var run = runs[r];
            var left = runs[r - 1];
            var right = runs[r + 1];
            if (run.Card is not { } observed || left.Card != right.Card) continue;
            if (left.Card == null && run.Count <= 2 && left.Count >= 2 && right.Count >= 2)
                anomalies.Add(new(run.Start, run.End, AnomalyKind.Ghost, observed, null));
            else if (left.Card is { } expected && run.Count == 1 && left.Count >= 2 && right.Count >= 2)
                anomalies.Add(new(run.Start, run.End, AnomalyKind.Outlier, observed, expected));
        }
        // A maximal A/B/A/B sequence of short runs is one oscillation event, not overlapping windows.
        for (var r = 0; r + 3 < runs.Count;)
        {
            var a = runs[r].Card;
            var b = runs[r + 1].Card;
            if (a == null || b == null || a == b || runs[r].Count > 3 || runs[r + 1].Count > 3) { r++; continue; }
            var end = r + 1;
            while (end + 1 < runs.Count && runs[end + 1].Count <= 3 && runs[end + 1].Card == ((end + 1 - r) % 2 == 0 ? a : b)) end++;
            if (end - r >= 3)
            {
                anomalies.Add(new(runs[r].Start, runs[end].End, AnomalyKind.FlipFlop, a.Value, b.Value));
                r = end + 1;
            }
            else r++;
        }
        for (var i = 0; i < frames.Count; i++)
        {
            if (!frames[i].Committed || frames[i].Card is not { } observed) continue;
            var votes = new Dictionary<Card, int>();
            var neighbours = 0;
            for (var n = Math.Max(0, i - 4); n <= Math.Min(frames.Count - 1, i + 4); n++)
            {
                if (n == i) continue;
                neighbours++;
                if (frames[n].Card is { } card) votes[card] = votes.GetValueOrDefault(card) + 1;
            }
            var majority = votes.OrderByDescending(v => v.Value).FirstOrDefault();
            if (majority.Value >= 3 && majority.Value > neighbours / 2 && majority.Key != observed)
                anomalies.Add(new(i, i, AnomalyKind.CommitMismatch, observed, majority.Key));
        }
        Anomalies = anomalies.OrderBy(a => a.Frame).ThenBy(a => a.Kind).ToArray();
    }

    public static SessionTotals Aggregate(IEnumerable<SessionAnalysis> sessions)
    {
        var pairs = new Dictionary<(Card, Card), int>();
        var ghosts = new Dictionary<Card, int>();
        var switches = new Dictionary<(Card, Card), int>();
        static (Card, Card) Pair(Card a, Card b) => a.ClassId < b.ClassId ? (a, b) : (b, a);
        foreach (var session in sessions)
        {
            var counted = new HashSet<(int, Card, Card)>();
            foreach (var anomaly in session.Anomalies)
            {
                if (anomaly.Kind == AnomalyKind.Ghost)
                    ghosts[anomaly.Observed] = ghosts.GetValueOrDefault(anomaly.Observed) + 1;
                else if (anomaly.Expected is { } expected)
                {
                    var pair = Pair(anomaly.Observed, expected);
                    if (counted.Add((anomaly.Frame, pair.Item1, pair.Item2)))
                        pairs[pair] = pairs.GetValueOrDefault(pair) + 1;
                }
            }
            for (var r = 1; r < session.Runs.Count; r++)
                if (session.Runs[r - 1].Card is { } from && session.Runs[r].Card is { } to)
                {
                    var pair = Pair(from, to);
                    switches[pair] = switches.GetValueOrDefault(pair) + 1;
                }
        }
        static PairCount[] Counts(Dictionary<(Card, Card), int> values) => values
            .OrderByDescending(v => v.Value).ThenBy(v => v.Key.Item1.ClassId).ThenBy(v => v.Key.Item2.ClassId)
            .Select(v => new PairCount(v.Key.Item1, v.Key.Item2, v.Value)).ToArray();
        return new(Counts(pairs), ghosts.OrderByDescending(v => v.Value).ThenBy(v => v.Key.ClassId)
            .Select(v => new CardCount(v.Key, v.Value)).ToArray(), Counts(switches));
    }
}
