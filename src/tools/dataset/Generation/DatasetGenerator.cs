using System.Globalization;
using System.Numerics;

namespace JassCardEye.Dataset.Generation;

/// <summary>Image format of the dataset.</summary>
public enum ImageFormat
{
    /// <summary>JPEG – small, lossy; standard for training datasets.</summary>
    Jpeg,

    /// <summary>PNG – lossless, but considerably larger.</summary>
    Png,
}

/// <summary>
/// Label variants to output. From one render run (the same images) only the label formats are
/// written differently, so the three approaches are fairly comparable.
/// </summary>
[Flags]
public enum DatasetTasks
{
    None = 0,

    /// <summary>Variant C: YOLO detector with 72 classes (box + card value in one step).</summary>
    Detect = 1,

    /// <summary>Variant B, stage 1: YOLO-OBB detector with 1 class (oriented localisation of the topmost card).</summary>
    Locate = 2,

    /// <summary>Variant A: classification of the whole image (72 classes plus none, no box).</summary>
    ClassifyFull = 4,

    /// <summary>Variant B, stage 2: classification of the rectified card crop (72 classes).</summary>
    ClassifyCrop = 8,

    All = Detect | Locate | ClassifyFull | ClassifyCrop,
}

/// <summary>Settings for a synthetic dataset run.</summary>
public sealed record DatasetOptions
{
    /// <summary>Target directory (gets images/ plus a subfolder per task).</summary>
    public required string OutputDir { get; init; }

    /// <summary>Root of the card scans (layout {deck}/{suit}/{rank}.jpg).</summary>
    public required string CardsRoot { get; init; }

    /// <summary>Optional folder with background images; otherwise procedural.</summary>
    public string? BackgroundsFolder { get; init; }

    /// <summary>
    /// Optional folder of real photographs without any card (data/negatives). When set, part of the
    /// negative frames are taken from these instead of being rendered.
    /// </summary>
    public string? RealNegativesFolder { get; init; }

    /// <summary>
    /// Share of the negative frames drawn from real photos rather than rendered scenes. Applies to
    /// the negatives only, so the overall positive/negative balance stays where
    /// <see cref="NegativeFraction"/> puts it.
    /// </summary>
    public float RealNegativeFraction { get; init; } = 0.25f;

    /// <summary>Number of images to generate.</summary>
    public int Count { get; init; } = 100;

    /// <summary>Seed for reproducibility.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Edge length of the square output images.</summary>
    public int ImageSize { get; init; } = 640;

    /// <summary>Fixed camera distance (smaller = closer); <c>null</c> = random per image.</summary>
    public float? CameraDistance { get; init; }

    /// <summary>Output image format (default JPEG).</summary>
    public ImageFormat ImageFormat { get; init; } = ImageFormat.Jpeg;

    /// <summary>JPEG quality 1..100 (only relevant for JPEG).</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>Which label variants are written.</summary>
    public DatasetTasks Tasks { get; init; } = DatasetTasks.All;

    /// <summary>
    /// A frame is only labelled when the top card lies <b>completely</b> inside the image, keeping
    /// this margin (in pixels) clear of the border. A cut-off card cannot be annotated correctly –
    /// two of its corners would be outside the frame – so such frames become negatives instead.
    /// </summary>
    public float FullyVisibleMarginPixels { get; init; } = 3f;

    /// <summary>
    /// A clipped top card only becomes a negative when at most this fraction of it is visible.
    /// Frames in between (nearly complete, but slightly cut) are ambiguous: labelling them either way
    /// would put almost identical images in opposite classes, so they are re-rolled instead.
    /// </summary>
    public float MaxVisibleForNegative { get; init; } = 0.85f;

    /// <summary>
    /// Fraction of images that are negatives (no valid top card): a motion-blurred thrown card over a
    /// pile, a blurred card passing over and covering the top card, or an empty table. The detectors
    /// get no box, the whole-image classifier the 'none' class.
    /// </summary>
    public float NegativeFraction { get; init; } = 0.3f;

    /// <summary>
    /// Fraction of the positives that additionally show a blurred card clipping in from the border
    /// while the top card stays clearly visible – these must still be detected.
    /// </summary>
    public float BorderFlyingFraction { get; init; } = 0.25f;

    /// <summary>
    /// Fraction of the positives whose top card carries a slight motion blur – readable at a glance,
    /// so these must still be detected. Keeps the model from equating "any blur" with "no card".
    /// </summary>
    public float SlightBlurFraction { get; init; } = 0.3f;

    /// <summary>
    /// Share of frames showing a squared deck instead of a scattered heap. Small on purpose: when
    /// cards are counted after a game they are thrown onto the pile, so a flush deck is the rare
    /// case - it only needs to be covered, not learned as the norm.
    /// </summary>
    public float AlignedStackFraction { get; init; } = 0.05f;

    /// <summary>
    /// How many images are rendered at once. 0 means one per processor. Rendering is CPU-bound and
    /// every image is independent, so this scales close to the core count.
    /// </summary>
    public int Parallelism { get; init; }

    /// <summary>Random ranges; defaults if not set.</summary>
    public RandomizerOptions? RandomizerOptions { get; init; }
}

/// <summary>
/// Generates, from a single render run, the training data for all three approaches and writes them
/// through the shared <see cref="VariantDatasetWriter"/> – identical layout to manually labelling
/// real photos.
/// </summary>
public sealed class DatasetGenerator
{
    private const int MaxVisibilityAttempts = 16;

    public void Generate(DatasetOptions options, Action<int, int>? onProgress = null)
    {
        int size = options.ImageSize;

        using var cards = new CardImageSource(options.CardsRoot);
        cards.Preload(); // decode all 72 scans before the threads start competing for them

        var backgrounds = new BackgroundSource(options.BackgroundsFolder);
        using var realNegatives = string.IsNullOrWhiteSpace(options.RealNegativesFolder)
            ? null
            : new RealNegativeSource(options.RealNegativesFolder);
        var randomizerOptions = options.RandomizerOptions ?? new RandomizerOptions();
        var parameters = new GenerationParameters { ImageSize = size, CameraDistance = options.CameraDistance };

        var writer = new VariantDatasetWriter(options.OutputDir, options.Tasks, options.ImageFormat, options.JpegQuality);

        int completed = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Parallelism > 0 ? options.Parallelism : Environment.ProcessorCount,
        };

        // Rendering is pure CPU work and every image is independent, so the images are produced in
        // parallel. Each one draws from its own random stream derived from (seed, index) – the result
        // is therefore identical no matter how many threads run, and image i depends only on its own
        // index rather than on the images before it.
        Parallel.For(0, options.Count, parallel, i =>
        {
            var rng = new Random(SampleSeed(options.Seed, i));
            var randomizer = new SceneRandomizer(randomizerOptions, backgrounds);
            var renderer = new SceneRenderer(cards, backgrounds);
            string stem = i.ToString("D6", CultureInfo.InvariantCulture);

            // Rare scene variants, drawn like every other property of the scene.
            var sampleParameters = parameters;
            if (rng.NextDouble() < options.AlignedStackFraction)
                sampleParameters = sampleParameters with { AlignedStack = true };

            // The deck and the camera tilt are the two properties of a scene that are not drawn -
            // see RunPlan. They are passed alongside the parameters rather than inside them so that
            // they survive the visibility re-roll in ResolveScene: a frame whose top card landed out
            // of view is resolved again, and keeps its deck and its place in the sweep.
            var plan = RunPlan.For(i, options.Count);
            var scene = ResolveScene(randomizer, sampleParameters, plan, rng, size, options, out bool usable);

            if (!usable || rng.NextDouble() < options.NegativeFraction)
            {
                // A share of the negatives is a real photograph of a card-free scene rather than a
                // rendered one - that is what teaches the detector that bright rectangles out in the
                // world are not cards.
                if (realNegatives is not null && rng.NextDouble() < options.RealNegativeFraction)
                {
                    using var photo = realNegatives.CreateSample(
                        rng, size, randomizerOptions.MinExposure, randomizerOptions.MaxExposure);
                    writer.WriteNegative(photo, stem);
                }
                else
                {
                    // No usable top card (mostly out of frame), or a deliberately negative frame.
                    scene = usable ? MakeNegative(randomizer, scene, rng) : scene;
                    using var negative = renderer.Render(scene);
                    writer.WriteNegative(negative.Image, stem);
                }
            }
            else
            {
                // A blurred card may clip in from the border – the top card stays readable, so this
                // is still a positive and must be detected.
                if (rng.NextDouble() < options.BorderFlyingFraction)
                    scene = randomizer.TryAddFlyingCard(scene, rng, occluding: false) ?? scene;

                // A slight motion blur on the top card itself – still easy to read, still a positive.
                // Kept well below the negatives' range so there is no ambiguous middle ground.
                if (rng.NextDouble() < options.SlightBlurFraction)
                    scene = scene with { TopCardMotion = SceneRandomizer.RandomMotion(rng, 0.02f, 0.09f) };

                using var result = renderer.Render(scene);
                writer.Write(result.Image, result.TopCardCorners, result.Scene.TopCard.Card, stem);
            }

            onProgress?.Invoke(Interlocked.Increment(ref completed), options.Count);
        });

        writer.WriteMetadata();
    }

    /// <summary>
    /// Deterministic per-image seed. Mixes (seed, index) with a splitmix64 step; a framework hash such
    /// as <see cref="HashCode.Combine(int, int)"/> must not be used here because it is randomised per
    /// process, which would destroy reproducibility.
    /// </summary>
    private static int SampleSeed(int seed, int index)
    {
        ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(uint)index + 0x165667B19E3779F9UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z & 0x7FFFFFFF);
    }

    // Turns a scene into a negative: an empty table, the pile's own top card thrown in mid-motion, or
    // a blurred card passing over and covering the top card. All three must yield "no detection".
    private static Scene MakeNegative(SceneRandomizer randomizer, Scene scene, Random rng)
    {
        double roll = rng.NextDouble();

        if (roll < 0.25)
            return scene with { Cards = [] }; // empty table

        if (roll < 0.65)
        {
            // The top card itself is being thrown – blurred beyond a reliable read. The range spans
            // clearly-too-blurred to heavily smeared, well above the slight blur used for positives.
            return scene with { TopCardMotion = SceneRandomizer.RandomMotion(rng, 0.25f, 0.65f) };
        }

        // A blurred card passes over the pile and covers the top card. Fall back to a thrown top card
        // when no covering placement was found.
        return randomizer.TryAddFlyingCard(scene, rng, occluding: true)
               ?? scene with { TopCardMotion = SceneRandomizer.RandomMotion(rng, 0.25f, 0.65f) };
    }

    // Resolves a scene and decides whether its top card may be labelled. Visibility is checked by
    // projection alone – no rendering needed. A card is labelled only when it is completely inside
    // the frame; a clearly clipped one becomes a negative. Frames in between are ambiguous and are
    // re-rolled, so almost identical images never end up in opposite classes.
    private static Scene ResolveScene(
        SceneRandomizer randomizer, GenerationParameters parameters, ScenePlan plan, Random rng,
        int size, DatasetOptions options, out bool usable)
    {
        Scene scene = randomizer.Resolve(parameters, plan, rng);
        for (int attempt = 0; attempt < MaxVisibilityAttempts; attempt++)
        {
            var camera = new PinholeCamera(scene.Camera, size);
            var corners = CardGeometry.ProjectCorners(scene.TopCard, camera);

            if (IsFullyInside(corners, size, options.FullyVisibleMarginPixels))
            {
                usable = true;
                return scene;
            }
            if (VisibleFraction(corners, size) <= options.MaxVisibleForNegative)
            {
                usable = false; // clearly cut off – a proper negative
                return scene;
            }
            if (attempt < MaxVisibilityAttempts - 1)
                scene = randomizer.Resolve(parameters, plan, rng);
        }
        usable = false; // ambiguous to the end – the conservative choice
        return scene;
    }

    // True when every corner of the card lies inside the image, keeping a margin clear of the border.
    private static bool IsFullyInside(Vector2[] corners, int size, float margin)
    {
        foreach (var c in corners)
            if (c.X < margin || c.Y < margin || c.X > size - margin || c.Y > size - margin)
                return false;
        return true;
    }

    // Fraction of the (axis-aligned hull of the) topmost card that lies within the image.
    private static float VisibleFraction(Vector2[] corners, int size)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            minX = MathF.Min(minX, c.X); minY = MathF.Min(minY, c.Y);
            maxX = MathF.Max(maxX, c.X); maxY = MathF.Max(maxY, c.Y);
        }
        float full = (maxX - minX) * (maxY - minY);
        if (full <= 0) return 0;

        float iw = MathF.Max(0, MathF.Min(maxX, size) - MathF.Max(minX, 0));
        float ih = MathF.Max(0, MathF.Min(maxY, size) - MathF.Max(minY, 0));
        return iw * ih / full;
    }
}
