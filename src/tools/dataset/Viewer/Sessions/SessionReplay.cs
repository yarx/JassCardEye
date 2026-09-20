using System;
using System.Collections.Generic;
using System.Linq;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>The app's two stability rules (<c>StabilityRule</c> in both apps).</summary>
internal enum StabilityRule { Run, Majority }

/// <summary>What a log is replayed with: the live threshold and the stability rule with its number of frames.</summary>
internal readonly record struct ReplaySettings(double Threshold, StabilityRule Rule, int Frames)
{
    /// <summary>The rule as the settings of the app describe it, in English.</summary>
    public string RuleText => Rule == StabilityRule.Run
        ? (Frames == 1 ? "run of 1 frame" : $"run of {Frames} frames")
        : $"majority, {Frames} of {WindowSize(Frames)} frames";

    /// <summary>The window of the majority rule: 2 × frames − 1, as <c>StabilityRule.windowSize</c> in the apps.</summary>
    public static int WindowSize(int frames) => Math.Max(1, frames * 2 - 1);
}

/// <summary>A card put on the pile, and the frame (from 0) that put it there.</summary>
internal sealed record CountedCard(int Frame, Card Card);

/// <summary>How one card fared in a session: how often it was the top detection, how confident the model got.</summary>
internal sealed record CardSummary(Card Card, int Frames, double Highest, int FramesAtThreshold, int LongestRunAtThreshold, int? CountedAt);

/// <summary>A card named for only a frame or two, at or above the threshold.</summary>
internal sealed record ShortReading(int Frame, Card Card, int Frames, double Highest);

/// <summary>
/// Replays a recognition log the way the app's pile decides (<c>PileTracker</c> in both apps), with settings
/// other than the ones it was recorded with - so a threshold or a stability rule can be tried on real
/// sessions from real devices before it goes into the app.
///
/// Two limits come from the log itself. It holds only detections at or above the threshold the app was
/// running with, so a lower threshold cannot be replayed. And it does not record a card removed by hand, so
/// the replay knows nothing of corrections.
/// </summary>
internal static class SessionReplay
{
    /// <summary>The lower edges of the confidence bands shown for a session; the last band is everything above 1.</summary>
    public static readonly double[] Bands = [0, 0.6, 0.7, 0.8, 0.9, 0.95, 1.0];

    /// <summary>The cards a pile with these settings would have counted, in order.</summary>
    public static IReadOnlyList<CountedCard> Count(IReadOnlyList<SessionFrame> frames, ReplaySettings settings)
    {
        var counted = new List<CountedCard>();
        var onPile = new HashSet<Card>();
        var recent = new Queue<Card?>();
        int window = ReplaySettings.WindowSize(settings.Frames);
        Card? candidate = null;
        int candidateCount = 0;

        foreach (var frame in frames)
        {
            Card? card = frame.Card is { } named && frame.Confidence >= settings.Threshold ? named : null;
            recent.Enqueue(card);
            while (recent.Count > window) recent.Dequeue();

            if (card != candidate)
            {
                candidate = card;
                candidateCount = 0;
            }
            if (card is not { } label) continue;
            candidateCount++;
            if (onPile.Contains(label)) continue;

            bool qualifies;
            if (settings.Rule == StabilityRule.Run)
            {
                qualifies = candidateCount >= settings.Frames;
            }
            else
            {
                // A card already on the pile is no rival: it stays in view while the next one is laid down.
                var tally = recent.OfType<Card>().GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
                qualifies = tally.GetValueOrDefault(label) >= settings.Frames &&
                            !tally.Any(t => t.Key != label && !onPile.Contains(t.Key) && t.Value > 1);
            }
            if (!qualifies) continue;
            onPile.Add(label);
            counted.Add(new CountedCard(frame.Index, label));
        }
        return counted;
    }

    /// <summary>
    /// The stability rule the app was counting with, found by replaying the log with each rule and 1 to 10
    /// frames until the counts fall on exactly the frames the app marked. Null when none does - after a
    /// card was removed by hand, for example. A run and a majority can explain the same log; the run wins.
    /// </summary>
    public static ReplaySettings? RecordedWith(IReadOnlyList<SessionFrame> frames)
    {
        var marked = frames.Where(f => f.Committed).Select(f => f.Index).ToArray();
        foreach (var rule in new[] { StabilityRule.Run, StabilityRule.Majority })
        {
            for (int count = 1; count <= 10; count++)
            {
                var settings = new ReplaySettings(0, rule, count);
                if (Count(frames, settings).Select(c => c.Frame).SequenceEqual(marked)) return settings;
            }
        }
        return null;
    }

    /// <summary>Every card that was the top detection at least once, the weakest first.</summary>
    public static IReadOnlyList<CardSummary> Cards(IReadOnlyList<SessionFrame> frames, double threshold)
    {
        var countedAt = new Dictionary<Card, int>();
        foreach (var frame in frames)
            if (frame.Committed && frame.Card is { } card) countedAt.TryAdd(card, frame.Index);

        var summaries = new List<CardSummary>();
        foreach (var group in frames.Where(f => f.Card is not null).GroupBy(f => f.Card!.Value))
        {
            int longest = 0, run = 0, previous = -2;
            foreach (var frame in group)
            {
                if (frame.Confidence < threshold)
                {
                    run = 0;
                    continue;
                }
                run = run > 0 && frame.Index == previous + 1 ? run + 1 : 1;
                previous = frame.Index;
                longest = Math.Max(longest, run);
            }
            summaries.Add(new CardSummary(group.Key, group.Count(), group.Max(f => f.Confidence),
                group.Count(f => f.Confidence >= threshold), longest,
                countedAt.TryGetValue(group.Key, out var at) ? at : null));
        }
        return summaries.OrderBy(s => s.Highest).ThenBy(s => s.Card.ClassId).ToArray();
    }

    /// <summary>Frames per confidence band (see <see cref="Bands"/>); frames without a detection are not in any.</summary>
    public static int[] Histogram(IReadOnlyList<SessionFrame> frames)
    {
        var counts = new int[Bands.Length];
        foreach (var frame in frames)
        {
            if (frame.Card is null) continue;
            int band = Bands.Length - 1;
            while (band > 0 && frame.Confidence < Bands[band]) band--;
            counts[band]++;
        }
        return counts;
    }

    /// <summary>
    /// Cards named for at most <paramref name="maxFrames"/> frames in a row, at or above the threshold, that
    /// differ from the nearest longer stretch on either side - a misreading while a card is laid down, or a
    /// card seen too briefly. They count only with a rule that short. A short stretch of the card that was
    /// already there before a misreading is its continuation, not another reading.
    /// </summary>
    public static IReadOnlyList<ShortReading> ShortReadings(IReadOnlyList<SessionFrame> frames, double threshold, int maxFrames = 2)
    {
        var runs = new List<(int Start, int End, Card? Card)>();
        for (int start = 0; start < frames.Count;)
        {
            Card? card = CardAt(start);
            int end = start;
            while (end + 1 < frames.Count && CardAt(end + 1) == card) end++;
            runs.Add((start, end, card));
            start = end + 1;
        }

        var readings = new List<ShortReading>();
        for (int r = 0; r < runs.Count; r++)
        {
            var (start, end, card) = runs[r];
            if (card is not { } named || end - start + 1 > maxFrames) continue;
            if (NearestLong(r, -1) is { } before && before.Card == card) continue;
            if (NearestLong(r, +1) is { } after && after.Card == card) continue;
            double highest = 0;
            for (int i = start; i <= end; i++) highest = Math.Max(highest, frames[i].Confidence);
            readings.Add(new ShortReading(start, named, end - start + 1, highest));
        }
        return readings;

        Card? CardAt(int index) => frames[index].Card is { } c && frames[index].Confidence >= threshold ? c : null;

        (int Start, int End, Card? Card)? NearestLong(int from, int step)
        {
            for (int r = from + step; r >= 0 && r < runs.Count; r += step)
                if (runs[r].End - runs[r].Start + 1 > maxFrames) return runs[r];
            return null;
        }
    }
}
