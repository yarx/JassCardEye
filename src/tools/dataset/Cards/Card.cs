namespace JassCardEye.Dataset.Cards;

/// <summary>
/// A single Jass card, uniquely determined by suit and rank. As a <c>readonly record struct</c>
/// it has value equality and can be used as a key in dictionaries or in sets without heap overhead.
/// </summary>
public readonly record struct Card(Suit Suit, Rank Rank)
{
    /// <summary>
    /// Stable YOLO class ID in the range 0..71. Order: suit first (Clubs, Diamonds, Hearts, Spades,
    /// then Acorns, Roses, Bells, Shields), within a suit the rank ascending (6 → Ace). This order is
    /// the contract basis for all labels and must not be changed, otherwise existing training data is
    /// mislabelled. It always holds that <c>JassDeck.Cards[c.ClassId] == c</c>.
    ///
    /// The German suits follow the French ones, so the French deck is 0..35 and the German deck 36..71.
    /// </summary>
    public int ClassId => (int)Suit * JassDeck.RankCount + (int)Rank;

    /// <summary>
    /// Canonical label of the form <c>suit_rank</c>, e.g. <c>clubs_6</c> or <c>roses_ace</c>. The
    /// suit tokens are unique across both decks, so the label needs no deck part and stays parsable
    /// by splitting at the first underscore. Matches the folder names under data/cards/{deck}/.
    /// </summary>
    public string Label => $"{Suit.ToToken()}_{Rank.ToToken()}";

    /// <summary>Short display for UI purposes, e.g. <c>6♣</c>, <c>ace♠</c> or <c>6Ro</c>.</summary>
    public string Display => $"{Rank.ToToken()}{Suit.ToSymbol()}";

    /// <summary>The deck this card belongs to.</summary>
    public Deck Deck => Suit.Deck();

    /// <inheritdoc cref="Label"/>
    public override string ToString() => Label;

    /// <summary>Creates the card for the given YOLO class ID (0..71).</summary>
    /// <exception cref="ArgumentOutOfRangeException">ID outside 0..71.</exception>
    public static Card FromClassId(int classId)
    {
        if (classId is < 0 or >= JassDeck.CardCount)
            throw new ArgumentOutOfRangeException(nameof(classId), classId,
                $"Class ID must be in the range 0..{JassDeck.CardCount - 1}.");

        return new Card((Suit)(classId / JassDeck.RankCount),
                        (Rank)(classId % JassDeck.RankCount));
    }

    /// <summary>Tries to parse a label of the form <c>suit_rank</c>.</summary>
    public static bool TryParse(string? label, out Card card)
    {
        card = default;
        if (string.IsNullOrWhiteSpace(label)) return false;

        var sep = label.IndexOf('_');
        if (sep <= 0 || sep >= label.Length - 1) return false;

        if (SuitExtensions.TryFromToken(label[..sep], out var suit) &&
            RankExtensions.TryFromToken(label[(sep + 1)..], out var rank))
        {
            card = new Card(suit, rank);
            return true;
        }
        return false;
    }

    /// <summary>Parses a label of the form <c>suit_rank</c> or throws.</summary>
    /// <exception cref="FormatException">Label does not match the format.</exception>
    public static Card Parse(string label) =>
        TryParse(label, out var card)
            ? card
            : throw new FormatException(
                $"'{label}' is not a valid card label (expected: suit_rank, e.g. clubs_6).");
}
