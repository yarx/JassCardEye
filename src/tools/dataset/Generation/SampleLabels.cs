using System.Globalization;
using System.Numerics;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// The hand-made annotation of a single dataset sample: which card lies on top, and where its four
/// corners are. It lives in two files - the detect label (class plus axis-aligned box) and the locate
/// label (the four corners in order) - and those two are the source of truth from which
/// <see cref="DatasetRebuilder"/> derives everything else.
///
/// Being able to read it back is what makes a label correctable at all. The axis-aligned box has lost
/// the corner order, so a card annotated starting at the wrong corner looks perfectly fine there; only
/// the oriented label still says which corner was meant to be the top left, and therefore only it can
/// reveal - and fix - a card that ends up rectified upside down.
/// </summary>
public static class SampleLabels
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>The sample's image in <c>images/</c>, or null when the stem is not in this dataset.</summary>
    public static string? FindImage(string root, string stem)
    {
        foreach (var ext in ImageExtensions)
        {
            var path = Path.Combine(root, "images", stem + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>True when the dataset keeps hand-made annotations that can be corrected.</summary>
    public static bool IsAnnotated(string root) => Directory.Exists(Path.Combine(root, "detect", "labels"));

    /// <summary>The annotated card, or null when the sample is a negative - a frame without a label.</summary>
    public static Card? ReadCard(string root, string stem)
    {
        var file = DetectFile(root, stem);
        if (!File.Exists(file)) return null;

        var parts = File.ReadAllText(file).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;
        return id >= 0 && id < JassDeck.CardCount ? Card.FromClassId(id) : null;
    }

    /// <summary>
    /// The four corners in pixel coordinates of an image of the given size, or an empty array when the
    /// sample has no oriented label to read them from.
    /// </summary>
    public static Vector2[] ReadCorners(string root, string stem, int width, int height)
    {
        var file = LocateFile(root, stem);
        if (!File.Exists(file)) return [];

        var parts = File.ReadAllText(file).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9) return [];

        var corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryParse(parts[1 + 2 * i], out var x) || !TryParse(parts[2 + 2 * i], out var y)) return [];
            corners[i] = new Vector2(x * width, y * height);
        }
        return corners;
    }

    /// <summary>
    /// Replaces the annotation of one sample. The image itself is deliberately left untouched: it is
    /// version-controlled, and re-encoding it would change the file without changing its content.
    /// The oriented label is only written where the dataset has that variant at all, so a correction
    /// cannot add a task the dataset was never built with.
    /// </summary>
    public static void Write(string root, string stem, Card card, Vector2[] corners, int width, int height)
    {
        if (corners.Length != 4) throw new ArgumentException("Four corners are required.", nameof(corners));

        WriteLine(DetectFile(root, stem), YoloBox.FromCorners(card.ClassId, corners, width, height).ToLine());
        WriteLine(LocateFile(root, stem), ObbLine(corners, width, height));
    }

    /// <summary>
    /// Turns the sample into a negative: both labels are removed. Ultralytics reads an image without a
    /// label file as background, which is exactly what a frame with no recognisable card is.
    /// </summary>
    public static void WriteNegative(string root, string stem)
    {
        Delete(DetectFile(root, stem));
        Delete(LocateFile(root, stem));
    }

    /// <summary>
    /// Removes the sample from the dataset altogether - the image and both labels. For a frame in the
    /// ambiguous band: a card that is nearly complete but touches the border. It cannot be annotated
    /// correctly, and it is too nearly whole to be filed as background - so it leaves the dataset, the
    /// counterpart of the generator drawing such a scene again. A card that is clearly cut off is a
    /// negative instead (<see cref="WriteNegative"/>), as the generator files it.
    ///
    /// Everything derived from the three files - the class folders, the per-variant image links -
    /// disappears with the rebuild the caller runs afterwards, which recreates those from scratch.
    /// </summary>
    public static void DeleteSample(string root, string stem)
    {
        Delete(DetectFile(root, stem));
        Delete(LocateFile(root, stem));
        if (FindImage(root, stem) is { } image) Delete(image);
    }

    /// <summary>Ultralytics OBB line: class 0 plus the four normalised corner points.</summary>
    public static string ObbLine(Vector2[] corners, int width, int height)
    {
        var parts = new List<string>(1 + corners.Length * 2) { "0" };
        foreach (var c in corners)
        {
            parts.Add(Normalize(c.X, width));
            parts.Add(Normalize(c.Y, height));
        }
        return string.Join(' ', parts);
    }

    private static string Normalize(float value, int extent) =>
        Math.Clamp(value / extent, 0f, 1f).ToString("0.000000", CultureInfo.InvariantCulture);

    private static string DetectFile(string root, string stem) =>
        Path.Combine(root, "detect", "labels", stem + ".txt");

    private static string LocateFile(string root, string stem) =>
        Path.Combine(root, "locate", "labels", stem + ".txt");

    // Only writes where the variant's labels folder already exists - see Write().
    private static void WriteLine(string path, string line)
    {
        if (!Directory.Exists(Path.GetDirectoryName(path)!)) return;
        File.WriteAllText(path, line + "\n");
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { /* variant not present - nothing to delete */ }
    }

    private static bool TryParse(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
