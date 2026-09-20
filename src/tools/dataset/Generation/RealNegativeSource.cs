using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Real photographs that contain no card at all, mixed into the dataset as negatives.
///
/// The rendered negatives (empty table, thrown card, covering card) all play on the generated table
/// with nothing but cards on it, so the detector never learns what the rest of the world looks like.
/// A bright rectangular object on a darker surface - a receipt, a sheet of paper, a blister pack - is
/// exactly the pattern it was taught to call a card, and it duly does. These photos are the counter
/// example, and they need no labels: for a detector a negative is simply an image without a box.
///
/// A handful of photos has to serve thousands of frames, so each use takes a random square sub-crop
/// in a random orientation with a random exposure. Any crop of a card-free photo is still card-free,
/// so the augmentation cannot introduce a wrong label.
/// </summary>
public sealed class RealNegativeSource : IDisposable
{
    private readonly SKImage[] _images;

    public RealNegativeSource(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Negatives folder not found: {folder}");

        _images = Directory.EnumerateFiles(folder)
            .Where(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)   // stable order keeps the seed reproducible
            .Select(Decode)
            .Where(image => image is not null)
            .Select(image => image!)
            .ToArray();

        if (_images.Length == 0)
            throw new InvalidOperationException($"No usable images in negatives folder: {folder}");
    }

    /// <summary>
    /// Renders one negative frame: a random square section of a random photo, turned and mirrored at
    /// random, scaled to <paramref name="size"/> and given the same exposure variation the rendered
    /// scenes get.
    /// </summary>
    public SKImage CreateSample(Random rng, int size, float minExposure, float maxExposure)
    {
        var source = _images[rng.Next(_images.Length)];

        // Square section covering 55-100 % of the shorter edge, placed anywhere inside the photo.
        int shortest = Math.Min(source.Width, source.Height);
        int side = (int)(shortest * (0.55 + rng.NextDouble() * 0.45));
        side = Math.Clamp(side, 16, shortest);
        int left = rng.Next(source.Width - side + 1);
        int top = rng.Next(source.Height - side + 1);
        var crop = SKRectI.Create(left, top, side, side);

        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // One of eight orientations (four rotations, optionally mirrored). Mirroring is safe here in
        // a way it is not for cards: a mirrored photo of a bathroom is still not a card, whereas a
        // mirrored card would be a different card.
        canvas.Translate(size / 2f, size / 2f);
        canvas.RotateDegrees(90 * rng.Next(4));
        if (rng.Next(2) == 0) canvas.Scale(-1, 1);
        canvas.Translate(-size / 2f, -size / 2f);

        float exposure = minExposure + (float)rng.NextDouble() * (maxExposure - minExposure);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(
            [
                exposure, 0, 0, 0, 0,
                0, exposure, 0, 0, 0,
                0, 0, exposure, 0, 0,
                0, 0, 0, 1, 0,
            ]),
        };
        canvas.DrawImage(source, crop, SKRect.Create(size, size),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

        return surface.Snapshot();
    }

    private static SKImage? Decode(string path)
    {
        using var data = SKData.Create(path);
        return SKImage.FromEncodedData(data);
    }

    public void Dispose()
    {
        foreach (var image in _images)
            image.Dispose();
    }
}
