using System.Globalization;
using System.Numerics;
using JassCardEye.Dataset.Cards;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Rebuilds the derived parts of a dataset from its source of truth – the images plus the detect and
/// locate labels. Everything else (the classification folders, the images links and the data.yaml
/// files) is reconstructable, so it does not belong in version control: it consists of hard links,
/// which Git would store as full copies, and of paths that are only valid on the machine that wrote
/// them.
/// </summary>
public sealed class DatasetRebuilder
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>Number of samples processed by a rebuild.</summary>
    public readonly record struct Result(int Positives, int Negatives);

    public Result Rebuild(string root, int cropHeight = 384, int jpegQuality = 90)
    {
        string imagesDir = Path.Combine(root, "images");
        if (!Directory.Exists(imagesDir))
            throw new DirectoryNotFoundException($"No images/ folder in {root}");

        string detectLabels = Path.Combine(root, "detect", "labels");
        string locateLabels = Path.Combine(root, "locate", "labels");
        string classifyFull = Path.Combine(root, "classify_full", "train");
        string classifyCrop = Path.Combine(root, "classify_crop", "train");

        // Start from scratch so removed or re-labelled samples cannot leave stale entries behind.
        FileLink.Remove(Path.Combine(root, "classify_full"));
        FileLink.Remove(Path.Combine(root, "classify_crop"));
        Directory.CreateDirectory(classifyFull);
        Directory.CreateDirectory(classifyCrop);

        int positives = 0, negatives = 0;
        foreach (var imagePath in Directory.EnumerateFiles(imagesDir)
                     .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(imagePath);
            string imageName = Path.GetFileName(imagePath);
            string detectFile = Path.Combine(detectLabels, stem + ".txt");

            // No detect label means the frame is a negative: no card to recognise.
            if (!File.Exists(detectFile))
            {
                LinkIntoClass(classifyFull, "none", imageName);
                negatives++;
                continue;
            }

            var card = Card.FromClassId(int.Parse(
                File.ReadAllText(detectFile).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
                CultureInfo.InvariantCulture));
            LinkIntoClass(classifyFull, card.Label, imageName);

            // The rectified crop comes from the oriented box – the four corners of the card.
            string locateFile = Path.Combine(locateLabels, stem + ".txt");
            if (File.Exists(locateFile))
            {
                using var data = SKData.Create(imagePath);
                using var image = SKImage.FromEncodedData(data)
                    ?? throw new InvalidOperationException($"Image could not be decoded: {imagePath}");

                var corners = ReadObbCorners(locateFile, image.Width, image.Height);
                string dir = Path.Combine(classifyCrop, card.Label);
                Directory.CreateDirectory(dir);

                using var crop = CardCrop.Rectify(image, corners, cropHeight);
                using var encoded = crop.Encode(SKEncodedImageFormat.Jpeg, jpegQuality);
                using var fs = File.Create(Path.Combine(dir, stem + ".jpg"));
                encoded.SaveTo(fs);
            }
            positives++;
        }

        WriteVariantMetadata(root, "detect", JassClasses.Names);
        WriteVariantMetadata(root, "locate", ["card"]);
        LinkValToTrain(Path.Combine(root, "classify_full"));
        LinkValToTrain(Path.Combine(root, "classify_crop"));

        return new Result(positives, negatives);
    }

    // Files the shared image under a class folder – as a link where possible, else as a copy.
    private static void LinkIntoClass(string trainDir, string className, string imageName)
    {
        string dir = Path.Combine(trainDir, className);
        Directory.CreateDirectory(dir);
        FileLink.LinkFile(
            Path.Combine(dir, imageName),
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(trainDir)!)!, "images", imageName));
    }

    private static Vector2[] ReadObbCorners(string labelPath, int width, int height)
    {
        var parts = File.ReadAllText(labelPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
            corners[i] = new Vector2(
                float.Parse(parts[1 + 2 * i], CultureInfo.InvariantCulture) * width,
                float.Parse(parts[2 + 2 * i], CultureInfo.InvariantCulture) * height);
        return corners;
    }

    // Recreates the variant's images link and data.yaml.
    private static void WriteVariantMetadata(string root, string sub, IReadOnlyList<string> names)
    {
        string dir = Path.Combine(root, sub);
        if (!Directory.Exists(Path.Combine(dir, "labels"))) return;

        FileLink.MirrorDirectory(Path.Combine(dir, "images"), Path.Combine(root, "images"));

        var inline = string.Join(", ", names.Select(n => $"'{n}'"));
        var yaml = string.Join("\n",
        [
            "# Auto-generated by JassCardEye",
            $"path: {Path.GetFullPath(dir)}",
            "train: images",
            "val: images",
            $"nc: {names.Count}",
            $"names: [{inline}]",
        ]) + "\n";
        File.WriteAllText(Path.Combine(dir, "data.yaml"), yaml);
    }

    private static void LinkValToTrain(string classifyDir) =>
        FileLink.MirrorDirectory(Path.Combine(classifyDir, "val"), Path.Combine(classifyDir, "train"));
}
