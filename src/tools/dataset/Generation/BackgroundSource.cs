using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Provides the background (table surface / Jass mat). Procedural patterns are laid onto the table
/// plane (z=0) as a uniformly tiling texture and projected with the same camera as the cards – so the
/// surface gets the same tilt and perspective. Optionally supplied image files are fitted flat.
/// </summary>
public sealed class BackgroundSource
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp"];
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly string[] _images;

    public BackgroundSource(string? folder)
    {
        _images = folder is not null && Directory.Exists(folder)
            ? Directory.GetFiles(folder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToArray()
            : [];
    }

    /// <summary>Are background images present?</summary>
    public bool HasImages => _images.Length > 0;

    /// <summary>Deterministically picks a background image based on a seed.</summary>
    public string PickImage(int seed) => _images[(int)((uint)seed % (uint)_images.Length)];

    /// <summary>Draws the background filling the canvas.</summary>
    public void Draw(SKCanvas canvas, BackgroundSpec spec, PinholeCamera camera, int size)
    {
        if (spec.ImagePath is not null && File.Exists(spec.ImagePath))
        {
            using var data = SKData.Create(spec.ImagePath);
            using var image = SKImage.FromEncodedData(data);
            if (image is not null)
            {
                DrawCover(canvas, image, size);
                return;
            }
        }

        DrawPlanePattern(canvas, spec.ProceduralSeed, camera, size);
    }

    // Procedural pattern as a tiling texture on the table plane, projected in perspective.
    private static void DrawPlanePattern(SKCanvas canvas, int seed, PinholeCamera camera, int size)
    {
        var pattern = TableTexture.Create(seed);
        try
        {
            var (world2pixel, worldRect) = PlaneProjection.ForCamera(camera, size);

            // Tile shader in world coordinates (one tile = WorldTileSize world units).
            // localMatrix maps texture space → world space: scale = world per texel.
            float s = pattern.WorldTileSize;
            var local = SKMatrix.Concat(
                SKMatrix.CreateRotationDegrees(pattern.RotationDegrees),
                SKMatrix.CreateScale(s / pattern.Tile.Width, s / pattern.Tile.Height));
            using var shader = pattern.Tile.ToShader(
                SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, Sampling, local);
            using var paint = new SKPaint { Shader = shader };

            canvas.Save();
            canvas.SetMatrix(world2pixel);
            canvas.DrawRect(worldRect, paint);
            canvas.Restore();
        }
        finally { pattern.Tile.Dispose(); }
    }

    // Scales a supplied image to cover the frame (centre-crop), flat.
    private static void DrawCover(SKCanvas canvas, SKImage image, int size)
    {
        float scale = MathF.Max((float)size / image.Width, (float)size / image.Height);
        float w = image.Width * scale;
        float h = image.Height * scale;
        float left = (size - w) / 2f;
        float top = (size - h) / 2f;
        canvas.DrawImage(image, new SKRect(left, top, left + w, top + h),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
    }
}
