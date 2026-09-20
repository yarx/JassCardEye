using System.Numerics;
using JassCardEye.Dataset.Cards;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Writes a dataset in the multi-variant layout (see context/architecture/data-pipeline.md). From
/// <b>one</b> annotation – source image, four corner points of the topmost card (top-left,
/// top-right, bottom-right, bottom-left, in pixel coordinates) and its class – all selected label
/// variants are derived. Used by both the synthetic <see cref="DatasetGenerator"/> and the manual
/// labeler in the Viewer, so the format is guaranteed to be identical.
/// </summary>
public sealed class VariantDatasetWriter
{
    private readonly string _root;
    private readonly DatasetTasks _tasks;
    private readonly SKEncodedImageFormat _encoding;
    private readonly string _ext;
    private readonly int _quality;
    private readonly int _outputSize;

    private readonly string _imagesDir;
    private readonly string _detectLabels;
    private readonly string _locateLabels;
    private readonly string _classifyFull;
    private readonly string _classifyCrop;

    /// <param name="outputSize">
    /// If &gt; 0, every image is normalised to this square size: the central square of the source,
    /// scaled (see <see cref="CenterCropSquare"/>). 0 keeps the source size.
    /// </param>
    public VariantDatasetWriter(string root, DatasetTasks tasks, ImageFormat format, int quality,
        int outputSize = 0)
    {
        _root = root;
        _tasks = tasks;
        (_encoding, _ext) = format == ImageFormat.Png
            ? (SKEncodedImageFormat.Png, ".png")
            : (SKEncodedImageFormat.Jpeg, ".jpg");
        _quality = format == ImageFormat.Png ? 100 : Math.Clamp(quality, 1, 100);
        _outputSize = outputSize;

        _imagesDir = Path.Combine(root, "images");
        _detectLabels = Path.Combine(root, "detect", "labels");
        _locateLabels = Path.Combine(root, "locate", "labels");
        _classifyFull = Path.Combine(root, "classify_full", "train");
        _classifyCrop = Path.Combine(root, "classify_crop", "train");

        Directory.CreateDirectory(_imagesDir);
        if (_tasks.HasFlag(DatasetTasks.Detect)) Directory.CreateDirectory(_detectLabels);
        if (_tasks.HasFlag(DatasetTasks.Locate)) Directory.CreateDirectory(_locateLabels);
        // Create the classification train dirs up front so mirroring train into val in WriteMetadata
        // works even when no positive (crop) sample was written yet – e.g. a negative saved first.
        if (_tasks.HasFlag(DatasetTasks.ClassifyFull)) Directory.CreateDirectory(_classifyFull);
        if (_tasks.HasFlag(DatasetTasks.ClassifyCrop)) Directory.CreateDirectory(_classifyCrop);

        // Stems already on disk. Writing one of them again must first remove its old artifacts,
        // otherwise a sample rewritten with a different verdict keeps stale labels (e.g. a frame
        // corrected from "card" to "no card" would keep its box).
        foreach (var f in Directory.EnumerateFiles(_imagesDir))
            _seen.Add(Path.GetFileNameWithoutExtension(f));
    }

    private readonly HashSet<string> _seen = [];
    private readonly Lock _seenLock = new();

    // Makes writing a stem idempotent: removes every artifact of a previous write of that stem.
    // Samples are written from several threads, so the bookkeeping is guarded; the files themselves
    // live at paths unique to their stem and therefore need no lock.
    private void EnsureClean(string stem)
    {
        lock (_seenLock)
        {
            if (_seen.Add(stem)) return; // first write of this stem – nothing to clean
        }

        Delete(Path.Combine(_detectLabels, stem + ".txt"));
        Delete(Path.Combine(_locateLabels, stem + ".txt"));
        RemoveFromClassDirs(_classifyFull, stem);
        RemoveFromClassDirs(_classifyCrop, stem);
    }

    // Removes the sample from every class folder (including 'none') of a classification task.
    private static void RemoveFromClassDirs(string trainDir, string stem)
    {
        if (!Directory.Exists(trainDir)) return;
        foreach (var classDir in Directory.EnumerateDirectories(trainDir))
            foreach (var f in Directory.EnumerateFiles(classDir, stem + ".*").ToList())
                Delete(f);
    }

    private static void Delete(string path)
    {
        // File.Delete is a no-op when the file is absent.
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { /* parent directory absent – nothing to delete */ }
    }

    /// <summary>Writes a sample whose source image is loaded from disk.</summary>
    public void Write(string sourceImagePath, Vector2[] corners, Card card, string stem)
    {
        using var data = SKData.Create(sourceImagePath);
        using var image = SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException($"Image could not be decoded: {sourceImagePath}");
        Write(image, corners, card, stem);
    }

    /// <summary>
    /// Writes a sample. <paramref name="corners"/> are four points (TL, TR, BR, BL) in pixel
    /// coordinates of <paramref name="image"/>.
    /// </summary>
    public void Write(SKImage image, Vector2[] corners, Card card, string stem)
    {
        EnsureClean(stem);
        var (canonical, c, created) = Normalize(image, corners);
        try
        {
            int w = canonical.Width, h = canonical.Height;
            string imageName = stem + _ext;

            Save(canonical, Path.Combine(_imagesDir, imageName));

            if (_tasks.HasFlag(DatasetTasks.Detect))
                File.WriteAllText(Path.Combine(_detectLabels, stem + ".txt"),
                    YoloBox.FromCorners(card.ClassId, c, w, h).ToLine() + "\n");

            if (_tasks.HasFlag(DatasetTasks.Locate))
                File.WriteAllText(Path.Combine(_locateLabels, stem + ".txt"),
                    SampleLabels.ObbLine(c, w, h) + "\n");

            if (_tasks.HasFlag(DatasetTasks.ClassifyFull))
                LinkIntoClass(card.Label, imageName);

            if (_tasks.HasFlag(DatasetTasks.ClassifyCrop))
            {
                string dir = Path.Combine(_classifyCrop, card.Label);
                Directory.CreateDirectory(dir);
                using var crop = CardCrop.Rectify(canonical, c);
                Save(crop, Path.Combine(dir, imageName));
            }
        }
        finally
        {
            if (created) canonical.Dispose();
        }
    }

    /// <summary>Writes a negative sample (no card / not recognisable) whose image is loaded from disk.</summary>
    public void WriteNegative(string sourceImagePath, string stem)
    {
        using var data = SKData.Create(sourceImagePath);
        using var image = SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException($"Image could not be decoded: {sourceImagePath}");
        WriteNegative(image, stem);
    }

    /// <summary>
    /// Writes a negative sample: the image goes to images/, the detectors (detect/locate) get no
    /// label file (Ultralytics treats a label-less image as background), and for the whole-image
    /// classifier the image is filed under the <c>none</c> class. The crop classifier gets nothing –
    /// there is no card to crop (variant B abstains via its stage-1 detector).
    /// </summary>
    public void WriteNegative(SKImage image, string stem)
    {
        EnsureClean(stem);
        var (canonical, _, created) = Normalize(image, []);
        try
        {
            string imageName = stem + _ext;
            Save(canonical, Path.Combine(_imagesDir, imageName));

            if (_tasks.HasFlag(DatasetTasks.ClassifyFull))
                LinkIntoClass("none", imageName);
        }
        finally
        {
            if (created) canonical.Dispose();
        }
    }

    /// <summary>Writes classes.txt, the data.yaml files and the hard-link folders (images/, val/).</summary>
    public void WriteMetadata()
    {
        JassClasses.WriteClassesTxt(Path.Combine(_root, "classes.txt"));
        if (_tasks.HasFlag(DatasetTasks.Detect)) WriteDetectDir("detect", JassClasses.Names);
        if (_tasks.HasFlag(DatasetTasks.Locate)) WriteDetectDir("locate", ["card"]);
        if (_tasks.HasFlag(DatasetTasks.ClassifyFull)) LinkValToTrain(Path.Combine(_root, "classify_full"));
        if (_tasks.HasFlag(DatasetTasks.ClassifyCrop)) LinkValToTrain(Path.Combine(_root, "classify_crop"));
    }

    private void Save(SKImage image, string path)
    {
        using var data = image.Encode(_encoding, _quality);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    // Brings the source image to the square output size by a centre crop, moving the corners along.
    // Returns created=true when a new image was allocated (must be disposed by the caller).
    private (SKImage image, Vector2[] corners, bool created) Normalize(SKImage src, Vector2[] corners)
    {
        if (_outputSize <= 0 || (src.Width == _outputSize && src.Height == _outputSize))
            return (src, corners, false);

        var (image, outCorners) = CenterCropSquare(src, corners, _outputSize);
        return (image, outCorners, true);
    }

    private static readonly SKSamplingOptions FitSampling = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// The central square of an image, scaled to size×size, with the corners moved along – the
    /// picture this writer stores for a non-square photo, and therefore the picture a model trained
    /// on this dataset has ever seen. Public because a label proposal has to ask its models about
    /// exactly that square; the caller owns the returned image.
    /// </summary>
    public static (SKImage Image, Vector2[] Corners) CenterCropSquare(SKImage src, Vector2[] corners, int size)
    {
        int s = Math.Min(src.Width, src.Height);
        float cx = (src.Width - s) / 2f;
        float cy = (src.Height - s) / 2f;
        float scale = (float)size / s;

        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawImage(src,
            new SKRect(cx, cy, cx + s, cy + s),
            new SKRect(0, 0, size, size),
            FitSampling);

        var outCorners = corners.Select(p => new Vector2((p.X - cx) * scale, (p.Y - cy) * scale)).ToArray();
        return (surface.Snapshot(), outCorners);
    }

    private void WriteDetectDir(string sub, IReadOnlyList<string> names)
    {
        string dir = Path.Combine(_root, sub);
        FileLink.MirrorDirectory(Path.Combine(dir, "images"), Path.Combine(_root, "images"));

        var namesInline = string.Join(", ", names.Select(n => $"'{n}'"));
        var yaml = string.Join("\n",
        [
            "# Auto-generated by JassCardEye",
            $"path: {Path.GetFullPath(dir)}",
            "train: images",
            "val: images",
            $"nc: {names.Count}",
            $"names: [{namesInline}]",
        ]) + "\n";
        File.WriteAllText(Path.Combine(dir, "data.yaml"), yaml);
    }

    // Files the shared image under a class folder – linked where possible, copied where not.
    private void LinkIntoClass(string className, string imageName)
    {
        string dir = Path.Combine(_classifyFull, className);
        Directory.CreateDirectory(dir);
        FileLink.LinkFile(Path.Combine(dir, imageName), Path.Combine(_imagesDir, imageName));
    }

    private static void LinkValToTrain(string classifyDir) =>
        FileLink.MirrorDirectory(Path.Combine(classifyDir, "val"), Path.Combine(classifyDir, "train"));
}
