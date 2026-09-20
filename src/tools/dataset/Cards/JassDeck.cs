namespace JassCardEye.Dataset.Cards;

/// <summary>
/// The cards of both Swiss Jass decks: 2 decks × 4 suits × 9 ranks = 72 classes.
///
/// A game is played with one deck, so most code wants <see cref="Of"/> rather than
/// <see cref="Cards"/>: a pile is drawn from one deck, and the largest possible pile is that deck's
/// 36 cards. <see cref="Cards"/> is the class list - every appearance the model has to tell apart.
/// </summary>
public static class JassDeck
{
    /// <summary>Number of suits in one deck (4).</summary>
    public const int SuitsPerDeck = 4;

    /// <summary>Number of ranks per suit (9).</summary>
    public const int RankCount = 9;

    /// <summary>Number of cards in one deck (36) - and so the largest pile that can lie on a table.</summary>
    public const int CardsPerDeck = SuitsPerDeck * RankCount;

    /// <summary>Number of decks (2: French and German).</summary>
    public const int DeckCount = 2;

    /// <summary>Total number of classes across both decks (72).</summary>
    public const int CardCount = DeckCount * CardsPerDeck;

    /// <summary>All eight suits in canonical order, French first.</summary>
    public static IReadOnlyList<Suit> Suits { get; } = Enum.GetValues<Suit>();

    /// <summary>The nine ranks in ascending order.</summary>
    public static IReadOnlyList<Rank> Ranks { get; } = Enum.GetValues<Rank>();

    /// <summary>The two decks in canonical order.</summary>
    public static IReadOnlyList<Deck> Decks { get; } = Enum.GetValues<Deck>();

    /// <summary>
    /// All 72 cards in canonical order. The index in this list equals the
    /// <see cref="Card.ClassId"/>: <c>Cards[i].ClassId == i</c>.
    /// </summary>
    public static IReadOnlyList<Card> Cards { get; } =
        (from suit in Suits
         from rank in Ranks
         select new Card(suit, rank)).ToArray();

    /// <summary>The 36 cards of one deck, in class-ID order - what a pile is dealt from.</summary>
    public static IReadOnlyList<Card> Of(Deck deck) => deck switch
    {
        Deck.French => _french,
        Deck.German => _german,
        _ => throw new ArgumentOutOfRangeException(nameof(deck), deck, null),
    };

    private static readonly Card[] _french = [.. Cards.Where(c => c.Suit.Deck() == Deck.French)];
    private static readonly Card[] _german = [.. Cards.Where(c => c.Suit.Deck() == Deck.German)];
}
