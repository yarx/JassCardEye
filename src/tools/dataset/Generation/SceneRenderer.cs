using System.Numerics;
using JassCardEye.Dataset.Cards;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Renders a <see cref="Scene"/> to a square image: background, underlying cards with soft contact
/// shadows, then the topmost card – each warped in perspective –, an optional motion-blurred card
/// flying over the scene, table-bound lighting and a camera vignette. Returns the image together
/// with the projected corners of the topmost card.
/// </summary>
public sealed class SceneRenderer(CardImageSource cards, BackgroundSource backgrounds)
{
    private readonly CardImageSource _cards = cards;
    private readonly BackgroundSource _backgrounds = backgrounds;
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    // Motion below this (as a multiple of the card's height) still counts as a card resting on the
    // pile with a shaky camera rather than one in flight.
    private const float RestingMotionLimit = 0.15f;

    public RenderResult Render(Scene scene)
    {
        AssertOneDeck(scene);

        int size = scene.ImageSize;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var sceneSurface = SKSurface.Create(info);
        var canvas = sceneSurface.Canvas;

        var camera = new PinholeCamera(scene.Camera, size);
        var (world2pixel, worldBounds) = PlaneProjection.ForCamera(camera, size);

        // 1. Background (on the table plane, same perspective).
        _backgrounds.Draw(canvas, scene.Background, camera, size);

        // 2. Cards bottom to top, each preceded by a soft contact shadow.
        var lightGround = world2pixel.MapPoint(scene.Light.GroundCenter.X, scene.Light.GroundCenter.Y);
        using var paint = new SKPaint { IsAntialias = true };

        Vector2[] topCorners = [];
        for (int i = 0; i < scene.Cards.Count; i++)
        {
            var placement = scene.Cards[i];
            var image = _cards.Load(placement.Card);
            var src = SourceCorners(image);
            var dst = CardGeometry.ProjectCorners(placement, camera);

            bool isTop = i == scene.Cards.Count - 1;
            bool smear = isTop && scene.TopCardMotion != Vector2.Zero;

            // A slightly blurred card is still lying on the pile and keeps its contact shadow; a
            // strongly smeared one is in the air, where a contact shadow would be wrong. A squared
            // deck rests on the table as one body, so only its bottom card casts that shadow -
            // stacking 36 of them would pile up into a black ring.
            bool castsShadow = !scene.AlignedStack || i == 0;
            if (castsShadow && (!smear || scene.TopCardMotion.Length() < RestingMotionLimit))
                DrawCardShadow(canvas, dst, lightGround, scene.Light.ShadowStrength, size,
                    scene.Light.ShadowSoftness);

            if (smear)
            {
                DrawCardSmear(canvas, image, src, dst, scene.TopCardMotion, size);
            }
            else
            {
                canvas.Save();
                canvas.SetMatrix(Homography.Compute(src, dst));
                if (scene.AlignedStack && !isTop)
                {
                    // Flush under the top card only a sliver of each card shows, and what shows is
                    // its cut edge, not its face. Tinting through the bitmap's alpha keeps the
                    // rounded corners while replacing the print with paper white.
                    using var edge = new SKPaint
                    {
                        IsAntialias = true,
                        ColorFilter = SKColorFilter.CreateBlendMode(EdgeColor(i), SKBlendMode.SrcIn),
                    };
                    canvas.DrawImage(image, 0, 0, Sampling, edge);
                }
                else
                {
                    canvas.DrawImage(image, 0, 0, Sampling, paint);
                }
                canvas.Restore();
            }

            if (isTop)
                topCorners = dst;
        }

        // 3. A card flying over the scene, motion-blurred, above everything else.
        if (scene.Flying is { } flying)
        {
            var image = _cards.Load(flying.Placement.Card);
            DrawCardSmear(canvas, image, SourceCorners(image),
                CardGeometry.ProjectCorners(flying.Placement, camera), flying.Motion, size);
        }

        // 4. Lighting on the table plane (falloff + subtle sheen), projected in perspective.
        DrawPlaneLight(canvas, world2pixel, worldBounds, scene.Light);

        using var composed = sceneSurface.Snapshot();

        // 5. Global exposure, then camera vignette.
        using var finalSurface = SKSurface.Create(info);
        var finalCanvas = finalSurface.Canvas;
        using (var exposurePaint = new SKPaint { ColorFilter = ExposureFilter(scene.Exposure) })
            finalCanvas.DrawImage(composed, 0, 0, Sampling, exposurePaint);
        DrawVignette(finalCanvas, size);

        return new RenderResult(finalSurface.Snapshot(), topCorners, scene);
    }

    /// <summary>
    /// A pile is dealt from one deck. Jass is played with the French deck or the German one, never
    /// with both on the table, so a mixed heap is a scene that cannot occur - and a frame showing
    /// one would be labelled with a card from a deck that is not in play.
    ///
    /// Checked here because this is the one place every scene passes through, and because the fault
    /// would otherwise be invisible: a label names only the topmost card, so a German six lying
    /// under a French pile survives every check the dataset makes of itself. Cheap next to drawing
    /// the frame, and a corrupt run should stop in its first second rather than after twenty minutes.
    /// </summary>
    private static void AssertOneDeck(Scene scene)
    {
        foreach (var placement in scene.Cards)
            Verify(placement.Card, "on the table");

        if (scene.Flying is { } flying)
            Verify(flying.Placement.Card, "flying over the table");

        void Verify(Card card, string where)
        {
            if (card.Deck != scene.Deck)
                throw new InvalidOperationException(
                    $"Mixed pile: {card.Label} ({card.Deck.ToToken()}) {where} in a "
                    + $"{scene.Deck.ToToken()} scene.");
        }
    }

    private static Vector2[] SourceCorners(SKImage image) =>
    [
        new(0, 0),
        new(image.Width, 0),
        new(image.Width, image.Height),
        new(0, image.Height),
    ];

    /// <summary>
    /// Paper white for the cut edge of a stacked card, alternating faintly per layer so the band of
    /// edges reads as many cards rather than one solid block.
    /// </summary>
    private static SKColor EdgeColor(int layer)
    {
        byte v = (byte)(layer % 2 == 0 ? 236 : 214);
        return new SKColor(v, (byte)(v - 3), (byte)(v - 9));
    }

    /// <summary>
    /// Draws a card that moved during the exposure. The warped card is rendered once into an
    /// offscreen layer and then blitted many times along the motion vector. Overlapping taps
    /// accumulate to an opaque core with translucent leading and trailing edges – a card that keeps
    /// its shape and presence while its detail smears, which is what a thrown card looks like in a
    /// video frame. <paramref name="motion"/> is a multiple of the card's own projected height.
    /// </summary>
    private static void DrawCardSmear(SKCanvas canvas, SKImage image, Vector2[] src, Vector2[] dst,
        Vector2 motion, int size)
    {
        var motionPx = motion * CardGeometry.ProjectedHeight(dst);

        float minX = dst.Min(p => p.X), maxX = dst.Max(p => p.X);
        float minY = dst.Min(p => p.Y), maxY = dst.Max(p => p.Y);
        float padX = MathF.Abs(motionPx.X) / 2f + 2f, padY = MathF.Abs(motionPx.Y) / 2f + 2f;

        var sweep = SKRect.Intersect(
            new SKRect(minX - padX, minY - padY, maxX + padX, maxY + padY),
            new SKRect(0, 0, size, size));
        if (sweep.Width < 1 || sweep.Height < 1) return;

        // Render the warped card once, in layer-local coordinates.
        int w = (int)MathF.Ceiling(sweep.Width), h = (int)MathF.Ceiling(sweep.Height);
        using var layer = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var local = new Vector2[4];
        for (int i = 0; i < 4; i++)
            local[i] = new Vector2(dst[i].X - sweep.Left, dst[i].Y - sweep.Top);
        using (var cardPaint = new SKPaint { IsAntialias = true })
        {
            layer.Canvas.SetMatrix(Homography.Compute(src, local));
            layer.Canvas.DrawImage(image, 0, 0, Sampling, cardPaint);
        }
        using var card = layer.Snapshot();

        // Tap spacing below a pixel avoids banding; the per-tap alpha is chosen so that a pixel
        // covered by every tap ends up ~97 % opaque.
        float length = motionPx.Length();
        int steps = Math.Clamp((int)MathF.Round(length), 6, 64);
        byte alpha = (byte)Math.Clamp(255f * (1f - MathF.Pow(0.03f, 1f / steps)), 6f, 255f);

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        for (int k = 0; k < steps; k++)
        {
            float t = steps == 1 ? 0f : (float)k / (steps - 1) - 0.5f;
            canvas.DrawImage(card, sweep.Left + motionPx.X * t, sweep.Top + motionPx.Y * t, Sampling, paint);
        }
    }

    // Soft shadow under a card: offset slightly away from the light and blurred.
    private static void DrawCardShadow(SKCanvas canvas, Vector2[] dst, SKPoint lightGround, float strength,
        int size, float softness)
    {
        var center = new SKPoint((dst[0].X + dst[2].X) / 2f, (dst[0].Y + dst[2].Y) / 2f);
        var away = new Vector2(center.X - lightGround.X, center.Y - lightGround.Y);
        float len = away.Length();
        var dir = len > 1e-3f ? away / len : new Vector2(0, 1);

        // A lamp throws the shadow clearly to one side; diffuse light leaves little more than a soft
        // darkening right under the card, so the offset shrinks as the blur grows.
        float soft = Math.Clamp(softness, 0f, 1f);
        float offset = size * 0.014f * (1f - 0.8f * soft);

        using var path = new SKPath();
        path.AddPoly(
        [
            new SKPoint(dst[0].X + dir.X * offset, dst[0].Y + dir.Y * offset),
            new SKPoint(dst[1].X + dir.X * offset, dst[1].Y + dir.Y * offset),
            new SKPoint(dst[2].X + dir.X * offset, dst[2].Y + dir.Y * offset),
            new SKPoint(dst[3].X + dir.X * offset, dst[3].Y + dir.Y * offset),
        ], close: true);

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)(150 * Math.Clamp(strength, 0f, 1f))),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * (0.005f + 0.014f * soft)),
        };
        canvas.DrawPath(path, paint);
    }

    // Table-bound lighting: brighter core around the light, falloff to residual brightness; plus sheen.
    private static void DrawPlaneLight(SKCanvas canvas, SKMatrix world2pixel, SKRect worldBounds, SceneLight light)
    {
        byte ambient = (byte)(Math.Clamp(light.Ambient, 0f, 1f) * 255);
        var center = new SKPoint(light.GroundCenter.X, light.GroundCenter.Y);

        canvas.Save();
        canvas.SetMatrix(world2pixel);

        // Falloff (multiplicative): core stays white, edge drops to residual brightness.
        using (var falloff = new SKPaint { BlendMode = SKBlendMode.Multiply })
        {
            falloff.Shader = SKShader.CreateRadialGradient(
                center, light.Radius,
                [SKColors.White, new SKColor(ambient, ambient, ambient)],
                [0f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(worldBounds, falloff);
        }

        // Subtle sheen (additive brightening) at the light centre.
        using (var sheen = new SKPaint { BlendMode = SKBlendMode.Screen })
        {
            sheen.Shader = SKShader.CreateRadialGradient(
                center, light.Radius * 0.55f,
                [new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)],
                [0f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(worldBounds, sheen);
        }

        // A second source from another direction only brightens - darkening twice would make the
        // table sooty, and a fill light in a room does not remove light, it adds it.
        if (light.FillStrength > 0f && light.FillRadius > 0f)
        {
            byte peak = (byte)Math.Clamp(light.FillStrength * 150f, 0f, 255f);
            using var fill = new SKPaint { BlendMode = SKBlendMode.Screen };
            fill.Shader = SKShader.CreateRadialGradient(
                new SKPoint(light.FillCenter.X, light.FillCenter.Y), light.FillRadius,
                [new SKColor(255, 255, 255, peak), new SKColor(255, 255, 255, 0)],
                [0f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(worldBounds, fill);
        }

        canvas.Restore();
    }

    // Gentle lens vignette: image centre unchanged, corners slightly darkened.
    private static void DrawVignette(SKCanvas canvas, int size)
    {
        using var paint = new SKPaint { BlendMode = SKBlendMode.Multiply };
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(size / 2f, size / 2f), size * 0.72f,
            [SKColors.White, new SKColor(200, 200, 200)],
            [0.55f, 1f], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, size, size, paint);
    }

    private static SKColorFilter ExposureFilter(float exposure)
    {
        float e = exposure;
        float[] matrix =
        [
            e, 0, 0, 0, 0,
            0, e, 0, 0, 0,
            0, 0, e, 0, 0,
            0, 0, 0, 1, 0,
        ];
        return SKColorFilter.CreateColorMatrix(matrix);
    }
}
