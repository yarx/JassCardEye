using System.Numerics;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Cuts the topmost card out of the rendered image and rectifies it in perspective to an upright
/// rectangle – the input for the classification stage of variant B. Mirrors what happens in the real
/// pipeline: the detector yields the quad, which is rectified to an upright card image.
/// </summary>
public static class CardCrop
{
    /// <summary>Card aspect ratio (width/height) for the crop.</summary>
    private const float Aspect = CardGeometry.WorldWidth / CardGeometry.WorldHeight;

    /// <summary>
    /// Rectifies the topmost card to an upright image of the given height (width follows from the
    /// aspect ratio).
    /// </summary>
    public static SKImage Rectify(SKImage scene, Vector2[] screenCorners, int height = 384)
    {
        int width = Math.Max(1, (int)MathF.Round(height * Aspect));
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        // Homography: the card's screen corners → upright crop rectangle.
        Vector2[] dst = [new(0, 0), new(width, 0), new(width, height), new(0, height)];
        var screen2crop = Homography.Compute(screenCorners, dst);

        var canvas = surface.Canvas;
        canvas.SetMatrix(screen2crop);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(scene, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);

        return surface.Snapshot();
    }
}
