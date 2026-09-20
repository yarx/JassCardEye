using System.Globalization;
using JassCardEye.Dataset.Cards;
using JassCardEye.Dataset.Generation;
using SkiaSharp;
using System.Text;

// JassCardEye.Dataset.Cli – CLI for generating synthetic training data, rebuilding the derived parts of
// a dataset, and inspecting the card definitions and the run plan. Run it from the repository root:
// the default paths (data/cards, data/negatives, output/dataset) are relative. Every option with its
// default is listed in src/tools/dataset/README.md.
//
//   dotnet run -- generate    [--out output/dataset] [--count 100] [--seed 1] [--size 640] …
//   dotnet run -- rebuild     [--dataset output/dataset]
//   dotnet run -- plan        [--count 1000] [--summary]
//   dotnet run -- classes     [--out classes.txt]
//   dotnet run -- backgrounds [--out output/backgrounds] [--count 8] [--size 1024] [--seed 1]

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    var options = ParseOptions(args[1..]);
    return args[0] switch
    {
        "generate"    => Generate(options),
        "rebuild"     => Rebuild(options),
        "backgrounds" => Backgrounds(options),
        "classes"     => Classes(options),
        "plan"        => Plan(options),
        "-h" or "--help" or "help" => PrintUsage(),
        var cmd => Fail($"Unknown command: {cmd}"),
    };
}

static int Generate(Dictionary<string, string> o)
{
    var datasetOptions = new DatasetOptions
    {
        OutputDir = o.GetValueOrDefault("out", "output/dataset"),
        CardsRoot = o.GetValueOrDefault("cards", "data/cards"),
        // Without --backgrounds the table patterns are generated on-the-fly per image.
        BackgroundsFolder = o.GetValueOrDefault("backgrounds"),
        Count = ParseInt(o, "count", 100),
        Seed = ParseInt(o, "seed", 1),
        ImageSize = ParseInt(o, "size", 640),
        CameraDistance = ParseFloatOrNull(o, "distance"),
        ImageFormat = o.GetValueOrDefault("format", "jpg").ToLowerInvariant() is "png"
            ? ImageFormat.Png
            : ImageFormat.Jpeg,
        JpegQuality = ParseInt(o, "quality", 90),
        Tasks = ParseTasks(o.GetValueOrDefault("tasks", "all")),
        NegativeFraction = ParseFloatOrNull(o, "negatives") ?? 0.3f,
        BorderFlyingFraction = ParseFloatOrNull(o, "border-flying") ?? 0.25f,
        SlightBlurFraction = ParseFloatOrNull(o, "slight-blur") ?? 0.3f,
        AlignedStackFraction = ParseFloatOrNull(o, "aligned-stacks") ?? 0.05f,
        RealNegativesFolder = o.TryGetValue("real-negatives", out var negatives)
            ? negatives
            : DefaultRealNegatives(),
        RealNegativeFraction = ParseFloatOrNull(o, "real-negative-share") ?? 0.25f,
        Parallelism = ParseInt(o, "parallel", 0),
    };

    if (datasetOptions.Tasks == DatasetTasks.None)
        return Fail("Unknown --tasks value. Allowed: a, b, c, detect, locate, classify-full, classify-crop, all (comma-separated).");

    if (!Directory.Exists(datasetOptions.CardsRoot))
        return Fail($"Cards folder not found: {datasetOptions.CardsRoot}");

    Console.WriteLine($"Generating {datasetOptions.Count} images ({datasetOptions.ImageSize}×{datasetOptions.ImageSize}) "
                      + $"into {datasetOptions.OutputDir} …");

    try
    {
        new DatasetGenerator().Generate(datasetOptions, (done, total) =>
        {
            if (done == total || done % 25 == 0)
                Console.WriteLine($"  {done}/{total}");
        });
    }
    catch (FileNotFoundException missing)
    {
        // Every scan is decoded before rendering starts, so a card that is not there stops the run
        // in its first second. Reported rather than thrown: a missing file is a setup mistake, and a
        // stack trace through the image cache says nothing a reader of it needs.
        return Fail(missing.Message);
    }

    Console.WriteLine($"Done. Tasks: {datasetOptions.Tasks}. One subfolder per variant under {datasetOptions.OutputDir}.");
    return 0;
}

static int Rebuild(Dictionary<string, string> o)
{
    string dir = o.GetValueOrDefault("dataset", "output/dataset");
    if (!Directory.Exists(Path.Combine(dir, "images")))
        return Fail($"No images/ folder in {dir}");

    Console.WriteLine($"Rebuilding the derived parts of {dir} …");
    var result = new DatasetRebuilder().Rebuild(dir);
    Console.WriteLine($"Done. {result.Positives} labelled card(s), {result.Negatives} negative(s).");
    return 0;
}

// data/negatives is used automatically when it exists, so a normal run picks up the real photos
// without an extra flag; --real-negatives points somewhere else, an empty value switches them off.
static string? DefaultRealNegatives() =>
    Directory.Exists("data/negatives") ? "data/negatives" : null;

// Maps comma-separated tokens (a/b/c or task names) to the task flags.
static DatasetTasks ParseTasks(string value)
{
    var tasks = DatasetTasks.None;
    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        tasks |= token.ToLowerInvariant() switch
        {
            "all"                        => DatasetTasks.All,
            "c" or "detect"              => DatasetTasks.Detect,
            "a" or "classify-full"       => DatasetTasks.ClassifyFull,
            "b"                          => DatasetTasks.Locate | DatasetTasks.ClassifyCrop,
            "locate"                     => DatasetTasks.Locate,
            "classify-crop"              => DatasetTasks.ClassifyCrop,
            _                            => DatasetTasks.None,
        };
    }
    return tasks;
}

static int Backgrounds(Dictionary<string, string> o)
{
    string outDir = o.GetValueOrDefault("out", "output/backgrounds");
    int count = ParseInt(o, "count", 8);
    int size = ParseInt(o, "size", 1024);
    int seed = ParseInt(o, "seed", 1);

    Directory.CreateDirectory(outDir);
    var rng = new Random(seed);

    Console.WriteLine($"Generating {count} table patterns ({size}×{size}) into {outDir} …");
    for (int i = 0; i < count; i++)
    {
        using var image = TableTexture.RenderPreview(size, rng.Next());
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(outDir, $"bg_{i:D3}.png");
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    Console.WriteLine("Done.");
    return 0;
}

static int Classes(Dictionary<string, string> o)
{
    Console.WriteLine($"Jass classes: {JassDeck.CardCount} "
                      + $"({JassDeck.DeckCount} decks × {JassDeck.SuitsPerDeck} suits × {JassDeck.RankCount} ranks)");
    foreach (var deck in JassDeck.Decks)
    {
        var cards = JassDeck.Of(deck);
        Console.WriteLine();
        Console.WriteLine($"  {deck.ToToken()} ({cards[0].ClassId}–{cards[^1].ClassId})");
        foreach (var card in cards)
            Console.WriteLine($"    {card.ClassId,2}  {card.Display,-8}  {card.Label}");
    }

    if (o.TryGetValue("out", out var path))
    {
        JassClasses.WriteClassesTxt(path);
        Console.WriteLine();
        Console.WriteLine($"classes.txt written: {path}");
    }
    return 0;
}

// Prints the deck and the camera tilt each image of a run would be given. Both are pure functions
// of the index and the count, so this needs no rendering and answers in milliseconds what a full run
// would otherwise take twenty minutes to show. src/tools/check_run_plan.py reads it in CI, and it is also how
// the distribution gets plotted for the documentation.
static int Plan(Dictionary<string, string> o)
{
    int count = ParseInt(o, "count", 1000);
    if (count <= 0)
        return Fail("--count must be positive.");

    if (o.ContainsKey("summary"))
    {
        // Machine-readable on purpose: src/tools/check_run_plan.py reads the range and the deck names
        // from here instead of knowing them, so the check cannot drift from the plan it is checking.
        Console.WriteLine($"count {count}");
        Console.WriteLine($"min {RunPlan.MinTiltDegrees.ToString("0.######", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"max {RunPlan.MaxTiltDegrees.ToString("0.######", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"decks {string.Join(' ', JassDeck.Decks.Select(d => d.ToToken()))}");
        return 0;
    }

    // One image per line, "<tilt> <deck>", in the order the images are generated - so a reader sees
    // both the values and the fact that consecutive images are neither consecutive angles nor the
    // same deck.
    var text = new StringBuilder();
    for (int i = 0; i < count; i++)
    {
        var plan = RunPlan.For(i, count);
        text.Append(plan.TiltDegrees.ToString("0.######", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(plan.Deck.ToToken())
            .Append('\n');
    }
    Console.Out.Write(text.ToString());
    return 0;
}

// --- small helpers ---

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
        string key = args[i][2..];
        // Flag at the end or followed by another --: treat as an empty value.
        string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i]
            : "";
        result[key] = value;
    }
    return result;
}

static float? ParseFloatOrNull(Dictionary<string, string> o, string key) =>
    o.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        ? v
        : null;

static int ParseInt(Dictionary<string, string> o, string key, int fallback) =>
    o.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        ? v
        : fallback;

static int PrintUsage()
{
    Console.WriteLine("""
        JassCardEye.Dataset.Cli

        Commands:
          generate [--out output/dataset] [--count 100] [--cards data/cards]
                   [--backgrounds <dir>] [--seed 1] [--size 640] [--distance <n>]
                   [--format jpg|png] [--quality 90] [--tasks all] [--negatives 0.3]
              Generates the training data for all three approaches from one render run.
              --tasks selects variants (comma-separated): a=classify whole image,
                b=localise + classify card crop, c=detector 72 classes, all (default).
              --negatives is the fraction of "no card" images (thrown card blurred beyond a read,
                a blurred card covering the top card, or an empty table); detectors get no box,
                the whole-image classifier the 'none' class.
              --border-flying is the fraction of positives where a blurred card only clips in from
                the border while the top card stays clearly visible – these must still be detected.
              --slight-blur is the fraction of positives whose top card is slightly motion-blurred
                but still easy to read – these must be detected too.
              --aligned-stacks is the fraction of frames showing a squared deck instead of a
                scattered heap: cards flush on top of each other, their cut edges forming a band
                below the top card. Rare in play, hence a small share by default.
              --real-negatives points at a folder of photographs without any card (default:
                data/negatives when present, "" to switch off). --real-negative-share is how much
                of the negatives they make up. Rendered negatives only ever show a table with
                cards on it; these teach that paper, receipts and boxes out in the world are not
                cards. Each use is a random square section in a random orientation, so a few
                photos cover many frames.
              Neither the camera tilt nor the deck is a knob: both come from the image's index.
                Every image gets its own angle and the angles sweep 0–60° from vertical evenly
                across the run, so a flat view is exactly as common as a steep one; and every
                second image is dealt from the German deck instead of the French one, so each deck
                gets half the run and an even sweep of its own. A pile is never mixed.
                `plan --count N` prints both.
              Table patterns are created on-the-fly; --backgrounds only to feed in own images.
              --distance fixes the camera proximity (smaller = closer, ~1.8 near … ~4.2 far).
              --format selects JPEG (small, default) or PNG (lossless, large).
              --parallel sets how many images render at once (default: one per processor).

          rebuild [--dataset output/dataset]
              Rebuilds the derived parts of a dataset (classification folders, images links,
              data.yaml) from images/ plus the detect and locate labels. Those derived parts are
              not version-controlled: they share the images through hard links, which Git would
              store as full copies, and data.yaml holds an absolute path.

          backgrounds [--out output/backgrounds] [--count 8] [--size 1024] [--seed 1]
              Generates table patterns as image files (preview only; not needed for the dataset).

          classes [--out classes.txt]
              Shows the 72 classes (both decks) and optionally writes classes.txt.

          plan [--count 1000] [--summary]
              Prints "<tilt> <deck>" per image, in generation order - what the index decides for a
              run of that size. --summary prints the tilt range and the deck names instead.
              src/tools/check_run_plan.py reads both from here rather than knowing them.
        """);
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
