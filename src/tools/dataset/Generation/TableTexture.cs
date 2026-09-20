using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// A seamlessly tileable pattern tile plus the world size of one tile and a rotation in the table
/// plane. The pattern is projected onto the table plane as a uniformly repeating texture and thus
/// gets the same perspective as the cards.
/// </summary>
public sealed record TablePattern(SKImage Tile, float WorldTileSize, float RotationDegrees);

/// <summary>
/// Generates uniformly repeating table patterns (Jass mat) as seamless tiles: solid colour, felt,
/// speckle, checker, tartan. The tiles have a card-appropriate world size – "the whole mat" with a
/// frame is never visible.
/// </summary>
public static class TableTexture
{
    /// <summary>Number of base styles.</summary>
    public const int StyleCount = 5;

    private const int TileSize = 256;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Creates a pattern tile with its world size from a seed.</summary>
    public static TablePattern Create(int seed)
    {
        var rng = new Random(seed);
        float rotation = (float)(rng.NextDouble() * 360.0);
        return rng.Next(StyleCount) switch
        {
            0 => Plain(rng, rotation),
            1 => Felt(rng, rotation),
            2 => Speckle(rng, rotation),
            3 => Checker(rng, rotation),
            _ => Tartan(rng, rotation),
        };
    }

    /// <summary>Flat preview: the tile repeated side by side (for viewing only).</summary>
    public static SKImage RenderPreview(int size, int seed, int tilesAcross = 4)
    {
        var pattern = Create(seed);
        try
        {
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            float px = (float)size / tilesAcross;
            // localMatrix maps texture space → device space: one tile (Tile.Width texels) = px pixels.
            var local = SKMatrix.CreateScale(px / pattern.Tile.Width, px / pattern.Tile.Height);
            using var shader = pattern.Tile.ToShader(
                SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, Sampling, local);
            using var paint = new SKPaint { Shader = shader };
            surface.Canvas.DrawRect(0, 0, size, size, paint);
            return surface.Snapshot();
        }
        finally { pattern.Tile.Dispose(); }
    }

    // --- Styles (all seamlessly tileable) ---

    private static TablePattern Plain(Random rng, float rotation) =>
        new(RenderTile(canvas => canvas.Clear(RandomClothColor(rng))), 1.0f, rotation);

    private static TablePattern Felt(Random rng, float rotation)
    {
        var baseColor = RandomClothColor(rng);
        var tile = RenderTile(canvas =>
        {
            canvas.Clear(baseColor);
            // Fine grain: random dots – tile seamlessly since there is no large-scale structure.
            using var paint = new SKPaint();
            for (int i = 0; i < 1500; i++)
            {
                float shade = rng.NextDouble() < 0.5 ? 0.75f : 1.3f;
                paint.Color = Shade(baseColor, shade).WithAlpha((byte)rng.Next(8, 32));
                canvas.DrawRect((float)rng.NextDouble() * TileSize, (float)rng.NextDouble() * TileSize, 1.5f, 1.5f, paint);
            }
        });
        return new TablePattern(tile, 0.5f, rotation);
    }

    private static TablePattern Speckle(Random rng, float rotation)
    {
        var baseColor = RandomClothColor(rng);
        var tile = RenderTile(canvas =>
        {
            canvas.Clear(baseColor);
            using var paint = new SKPaint { IsAntialias = true };
            int count = rng.Next(120, 320);
            for (int i = 0; i < count; i++)
            {
                float r = 1.5f + (float)rng.NextDouble() * 3.5f;
                // Keep the centre away from the edge so no dot crosses the tile boundary (seamless).
                float x = r + (float)rng.NextDouble() * (TileSize - 2 * r);
                float y = r + (float)rng.NextDouble() * (TileSize - 2 * r);
                float shade = rng.NextDouble() < 0.5 ? 0.6f : 1.5f;
                paint.Color = Shade(baseColor, shade).WithAlpha((byte)rng.Next(25, 70));
                canvas.DrawCircle(x, y, r, paint);
            }
        });
        return new TablePattern(tile, 0.55f, rotation);
    }

    private static TablePattern Checker(Random rng, float rotation)
    {
        var c1 = RandomClothColor(rng);
        var c2 = Shade(c1, rng.NextDouble() < 0.5 ? 0.7f : 1.4f);
        var tile = RenderTile(canvas =>
        {
            canvas.Clear(c1);
            using var paint = new SKPaint { Color = c2 };
            float h = TileSize / 2f;
            canvas.DrawRect(0, 0, h, h, paint);
            canvas.DrawRect(h, h, h, h, paint);
        });
        float cellWorld = 0.22f + (float)rng.NextDouble() * 0.23f; // ≈ 20–40 mm
        return new TablePattern(tile, 2f * cellWorld, rotation);
    }

    private static TablePattern Tartan(Random rng, float rotation)
    {
        var baseColor = RandomClothColor(rng);
        var tile = RenderTile(canvas =>
        {
            canvas.Clear(baseColor);
            using var light = new SKPaint { Color = new SKColor(255, 255, 255, (byte)rng.Next(25, 55)) };
            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, (byte)rng.Next(25, 55)) };
            float w = TileSize * (0.06f + 0.05f * (float)rng.NextDouble());
            // Bands fully inside the tile → seamless when repeated.
            foreach (float p in new[] { TileSize * 0.15f, TileSize * 0.6f })
            {
                canvas.DrawRect(p, 0, w, TileSize, light);
                canvas.DrawRect(0, p, TileSize, w, light);
                canvas.DrawRect(p + w, 0, w * 0.5f, TileSize, dark);
                canvas.DrawRect(0, p + w, TileSize, w * 0.5f, dark);
            }
        });
        return new TablePattern(tile, 0.45f + (float)rng.NextDouble() * 0.2f, rotation);
    }

    // --- Building blocks ---

    private static SKImage RenderTile(Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        draw(surface.Canvas);
        return surface.Snapshot();
    }

    private static SKColor RandomClothColor(Random rng)
    {
        // Plausible cloth/felt colours: green, blue, red, teal, violet, occasionally grey.
        float[] hues = [130, 145, 160, 210, 225, 0, 350, 275, 180, 200];
        float h = hues[rng.Next(hues.Length)] + (float)(rng.NextDouble() * 16 - 8);
        float s = rng.Next(8) == 0 ? 6 : 30 + (float)rng.NextDouble() * 35; // occasionally almost grey
        float v = 28 + (float)rng.NextDouble() * 30;
        return SKColor.FromHsv((h % 360 + 360) % 360, s, v);
    }

    private static SKColor Shade(SKColor c, float factor) => new(
        (byte)Math.Clamp(c.Red * factor, 0, 255),
        (byte)Math.Clamp(c.Green * factor, 0, 255),
        (byte)Math.Clamp(c.Blue * factor, 0, 255),
        c.Alpha);
}
