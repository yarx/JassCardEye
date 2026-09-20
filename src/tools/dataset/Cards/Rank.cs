namespace JassCardEye.Dataset.Cards;

/// <summary>
/// The nine card ranks of a Jass deck, in ascending order. The order of the values is part of the
/// class-ID contract (see <see cref="Card.ClassId"/>) and must not be changed. Both decks share
/// these ranks and the names players use for them (Under, Ober, König, Ass); only the print differs:
/// the French deck shows Bauer and Dame where the German deck shows Under and Ober, and the German ten
/// is the Banner.
/// </summary>
public enum Rank
{
    /// <summary>6</summary>
    Six,

    /// <summary>7</summary>
    Seven,

    /// <summary>8</summary>
    Eight,

    /// <summary>9 (Nell when trump)</summary>
    Nine,

    /// <summary>10 (Banner)</summary>
    Ten,

    /// <summary>Under / Jack (Under on a German card)</summary>
    Jack,

    /// <summary>Ober / Queen (Ober on a German card)</summary>
    Queen,

    /// <summary>King</summary>
    King,

    /// <summary>Ace</summary>
    Ace,
}

/// <summary>Helper methods mapping <see cref="Rank"/> to tokens.</summary>
public static class RankExtensions
{
    /// <summary>
    /// Lower-case token for labels, file names and folders under data/cards/{deck}/{suit}/
    /// (6, 7, 8, 9, 10, jack, queen, king, ace). Both decks use these: on a German card jack is the
    /// Under, queen the Ober, king the König and ace the Ass.
    /// </summary>
    public static string ToToken(this Rank rank) => rank switch
    {
        Rank.Six   => "6",
        Rank.Seven => "7",
        Rank.Eight => "8",
        Rank.Nine  => "9",
        Rank.Ten   => "10",
        Rank.Jack  => "jack",
        Rank.Queen => "queen",
        Rank.King  => "king",
        Rank.Ace   => "ace",
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null),
    };

    /// <summary>Inverse of <see cref="ToToken"/>. Returns <c>false</c> for an unknown token.</summary>
    public static bool TryFromToken(string token, out Rank rank)
    {
        switch (token)
        {
            case "6":     rank = Rank.Six;   return true;
            case "7":     rank = Rank.Seven; return true;
            case "8":     rank = Rank.Eight; return true;
            case "9":     rank = Rank.Nine;  return true;
            case "10":    rank = Rank.Ten;   return true;
            case "jack":  rank = Rank.Jack;  return true;
            case "queen": rank = Rank.Queen; return true;
            case "king":  rank = Rank.King;  return true;
            case "ace":   rank = Rank.Ace;   return true;
            default:      rank = default;    return false;
        }
    }
}
