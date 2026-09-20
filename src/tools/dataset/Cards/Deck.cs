namespace JassCardEye.Dataset.Cards;

/// <summary>
/// The two decks a Swiss Jass is played with. Which one is on the table is a property of the game,
/// not of a single card: a pile is all French or all German, never a mixture.
/// </summary>
public enum Deck
{
    /// <summary>French-suited: clubs, diamonds, hearts, spades. Class IDs 0–35.</summary>
    French,

    /// <summary>German-suited: acorns, roses, bells, shields. Class IDs 36–71.</summary>
    German,
}

/// <summary>Helper methods mapping <see cref="Deck"/> to tokens.</summary>
public static class DeckExtensions
{
    /// <summary>Lower-case token for folders under data/cards/ and for reports (french, german).</summary>
    public static string ToToken(this Deck deck) => deck switch
    {
        Deck.French => "french",
        Deck.German => "german",
        _ => throw new ArgumentOutOfRangeException(nameof(deck), deck, null),
    };
}
