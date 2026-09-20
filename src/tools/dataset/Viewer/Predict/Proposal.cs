using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Viewer.Predict;

/// <summary>One oriented box B₁ found, in the pixels of the square it was asked about.</summary>
internal readonly record struct BoxScore(Vector2[] Corners, float Confidence);

/// <summary>What B₂ makes of one crop, for one class.</summary>
internal readonly record struct ClassScore(string Name, float Confidence);

/// <summary>
/// A label the models propose: corners in the photo's pixels, in the order a label is written in,
/// and the card to preselect - or none, when the class was not clear enough to be worth a click.
/// <see cref="Note"/> is what the person should look at; it is never a reason to stop.
/// </summary>
internal sealed record Proposal(
    Vector2[] Corners, float BoxConfidence, Card? Card, float ClassConfidence, string Note)
{
    /// <summary>
    /// The one line the status bar shows. It always says what to do with it: a proposal that reads
    /// like a result is one somebody saves without looking.
    /// </summary>
    public string Describe() => ProposalRules.Join(
        Card is { } card
            ? $"proposal: {card.Label} · card {BoxConfidence:0.00} · class {ClassConfidence:0.00} – check it"
            : $"proposal: corners only · card {BoxConfidence:0.00} – check it",
        Note);
}

/// <summary>
/// What became of the proposed corners. The three kinds are told apart because they mean different
/// things about the models: <see cref="Moved"/> says B₁ put a corner in the wrong place, while
/// <see cref="Turned"/> says only that the half turn was guessed wrong - the corners were right, and
/// the guess is the one thing in a proposal that nothing could decide (see <c>ObbCorners</c>).
/// </summary>
internal enum CornerChange { AsProposed, Turned, Reordered, Moved }

/// <summary>
/// Every decision between what the two models answer and what appears on screen. It sits in the tool
/// rather than in the helper script because these are rules about a person's work, not about a
/// model - and because CI can check a rule that lives here (see <c>src/tools/dataset/Checks</c>).
///
/// The thresholds are set by hand, not measured: they are the point at which a preselection is
/// judged to help more than it costs. A proposal that stays below them leaves the photo as empty as
/// it was, which is the harmless outcome: the only expensive mistake here is a wrong label that
/// looks like a checked one.
/// </summary>
internal static class ProposalRules
{
    /// <summary>Below this, B₁'s box is not worth drawing: no corners, nothing placed.</summary>
    public const float MinBoxConfidence = 0.50f;

    /// <summary>Below this, the corners are placed but the class is left to the person.</summary>
    public const float MinClassConfidence = 0.60f;

    /// <summary>How close two answers have to be before the person is told they were close.</summary>
    public const float CloseMargin = 0.10f;

    /// <summary>
    /// The box a proposal is built on: the strongest, and only if it clears the gate. A runner-up
    /// almost as strong is worth saying - two cards that close together is exactly the moment when
    /// "the top one" is a matter of opinion.
    /// </summary>
    public static (BoxScore Box, string Note)? PickBox(IReadOnlyList<BoxScore> boxes)
    {
        if (boxes.Count == 0) return null;

        var best = boxes.MaxBy(b => b.Confidence);
        if (best.Confidence < MinBoxConfidence) return null;

        var runnerUp = boxes.Where(b => b.Confidence < best.Confidence).Select(b => b.Confidence)
            .DefaultIfEmpty(0f).Max();
        var note = best.Confidence - runnerUp <= CloseMargin ? "a second card is nearly as strong" : "";
        return (best, note);
    }

    /// <summary>
    /// The card to preselect, of the deck being labelled. Never of the other one: a labelling session
    /// is one deck, and a deck switched by a proposal is the mistake this picker was built to
    /// prevent. When the other deck holds a stronger answer, that is said instead - it usually means
    /// the deck is set wrong, which is a thing to notice rather than to correct silently.
    /// </summary>
    public static (Card? Card, float Confidence, string Note) PickCard(IReadOnlyList<ClassScore> scores, Deck deck)
    {
        var ofDeck = BestOfDeck(scores, deck);
        if (ofDeck is not { } best) return (null, 0f, "B₂ knows no card of this deck");

        var card = Card.Parse(best.Name);
        var overall = scores.MaxBy(s => s.Confidence);
        var note = Card.TryParse(overall.Name, out var other) && other.Deck != deck &&
                   overall.Confidence > best.Confidence
            ? $"{overall.Name} in the other deck is stronger"
            : "";

        return best.Confidence < MinClassConfidence
            ? (null, best.Confidence, Join($"the class stays open, {best.Name} only reaches {best.Confidence:0.00}", note))
            : (card, best.Confidence, note);
    }

    /// <summary>
    /// How the corners of a label being saved relate to the ones that were proposed. The same four
    /// points in another order are not a correction of the box but of the corner it starts at, and
    /// counting those apart is what turns the proposal log into a measurement.
    /// </summary>
    public static CornerChange Compare(IReadOnlyList<Vector2> proposed, IReadOnlyList<Vector2> saved)
    {
        if (proposed.Count != 4 || saved.Count != 4) return CornerChange.Moved;

        for (int turn = 0; turn < 4; turn++)
        {
            if (!Enumerable.Range(0, 4).All(i => proposed[(i + turn) % 4] == saved[i])) continue;
            return turn switch
            {
                0 => CornerChange.AsProposed,
                2 => CornerChange.Turned,
                _ => CornerChange.Reordered,
            };
        }
        return CornerChange.Moved;
    }

    /// <summary>The strongest class of one deck, or null when the scores hold no card of it.</summary>
    public static ClassScore? BestOfDeck(IReadOnlyList<ClassScore> scores, Deck deck)
    {
        var ofDeck = scores.Where(s => Card.TryParse(s.Name, out var card) && card.Deck == deck).ToList();
        return ofDeck.Count == 0 ? null : ofDeck.MaxBy(s => s.Confidence);
    }

    /// <summary>Joins the notes that have something to say, in the order they were made.</summary>
    public static string Join(params string[] notes) =>
        string.Join(" · ", notes.Where(n => !string.IsNullOrEmpty(n)));
}
