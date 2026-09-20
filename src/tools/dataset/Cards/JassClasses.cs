namespace JassCardEye.Dataset.Cards;

/// <summary>
/// Canonical YOLO class list. The index equals the class ID and thus <see cref="Card.ClassId"/>.
/// This list is the single source of truth for classes.txt and data.yaml – training, labelling and
/// display code derive their class names from here, not from their own constants.
/// </summary>
public static class JassClasses
{
    /// <summary>The 72 class names in class-ID order (clubs_6 … shields_ace).</summary>
    public static IReadOnlyList<string> Names { get; } =
        JassDeck.Cards.Select(c => c.Label).ToArray();

    /// <summary>Writes the class names line by line into a classes.txt (one class per line).</summary>
    public static void WriteClassesTxt(string path) =>
        File.WriteAllLines(path, Names);
}
