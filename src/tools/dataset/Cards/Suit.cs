namespace JassCardEye.Dataset.Cards;

/// <summary>
/// The suits of both Jass decks: the four French ones first, then the four German ones.
///
/// The order of the values is part of the class-ID contract (see <see cref="Card.ClassId"/>) and
/// must not be changed. The German suits follow the French ones rather than being interleaved:
/// with <c>ClassId = suit × 9 + rank</c> the French deck takes 0–35 and the German deck 36–71, so
/// each deck is one contiguous block of class IDs.
/// </summary>
public enum Suit
{
    /// <summary>Clubs ♣ (French)</summary>
    Clubs,

    /// <summary>Diamonds ♦ (French)</summary>
    Diamonds,

    /// <summary>Hearts ♥ (French)</summary>
    Hearts,

    /// <summary>Spades ♠ (French)</summary>
    Spades,

    /// <summary>Acorns / Eichel (German)</summary>
    Acorns,

    /// <summary>Roses / Rosen (German)</summary>
    Roses,

    /// <summary>Bells / Schellen (German)</summary>
    Bells,

    /// <summary>Shields / Schilten (German)</summary>
    Shields,
}

/// <summary>Helper methods mapping <see cref="Suit"/> to tokens, symbols and its deck.</summary>
public static class SuitExtensions
{
    /// <summary>
    /// Lower-case token for labels, file names and folders under data/cards/{deck}/
    /// (clubs, diamonds, hearts, spades, acorns, roses, bells, shields).
    ///
    /// The tokens are unique across both decks, which is what lets a label stay <c>suit_rank</c>
    /// with no deck prefix - the app splits a label at its first underscore and would have to be
    /// taught otherwise.
    /// </summary>
    public static string ToToken(this Suit suit) => suit switch
    {
        Suit.Clubs    => "clubs",
        Suit.Diamonds => "diamonds",
        Suit.Hearts   => "hearts",
        Suit.Spades   => "spades",
        Suit.Acorns   => "acorns",
        Suit.Roses    => "roses",
        Suit.Bells    => "bells",
        Suit.Shields  => "shields",
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, null),
    };

    /// <summary>
    /// Short display form: the Unicode symbol for the French suits (♣ ♦ ♥ ♠), and for the German
    /// ones the abbreviation Swiss players use anyway (Ei, Ro, Sche, Schi) - Unicode has no glyphs
    /// for acorns, roses, bells and shields.
    /// </summary>
    public static string ToSymbol(this Suit suit) => suit switch
    {
        Suit.Clubs    => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts   => "♥",
        Suit.Spades   => "♠",
        Suit.Acorns   => "Ei",
        Suit.Roses    => "Ro",
        Suit.Bells    => "Sche",
        Suit.Shields  => "Schi",
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, null),
    };

    /// <summary>
    /// The deck this suit belongs to. Spelled out rather than derived from the enum's order: the
    /// order is a contract, but reading membership out of it would silently file a third deck's
    /// suits under German, where this throws.
    /// </summary>
    public static Deck Deck(this Suit suit) => suit switch
    {
        Suit.Clubs or Suit.Diamonds or Suit.Hearts or Suit.Spades  => Cards.Deck.French,
        Suit.Acorns or Suit.Roses or Suit.Bells or Suit.Shields    => Cards.Deck.German,
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, null),
    };

    /// <summary>Inverse of <see cref="ToToken"/>. Returns <c>false</c> for an unknown token.</summary>
    public static bool TryFromToken(string token, out Suit suit)
    {
        switch (token)
        {
            case "clubs":    suit = Suit.Clubs;    return true;
            case "diamonds": suit = Suit.Diamonds; return true;
            case "hearts":   suit = Suit.Hearts;   return true;
            case "spades":   suit = Suit.Spades;   return true;
            case "acorns":   suit = Suit.Acorns;   return true;
            case "roses":    suit = Suit.Roses;    return true;
            case "bells":    suit = Suit.Bells;    return true;
            case "shields":  suit = Suit.Shields;  return true;
            default:         suit = default;       return false;
        }
    }
}
