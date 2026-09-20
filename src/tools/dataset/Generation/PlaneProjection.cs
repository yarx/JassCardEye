using System.Numerics;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Projection of the table plane (z=0) onto the image: returns the world→pixel homography and the
/// visible world region as a rectangle. Used for background patterns and lighting so both get the
/// same perspective as the cards.
/// </summary>
public static class PlaneProjection
{
    public static (SKMatrix WorldToPixel, SKRect WorldBounds) ForCamera(
        PinholeCamera camera, int size, float marginFraction = 0.02f)
    {
        Vector2[] refWorld = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var refPixel = new Vector2[4];
        for (int i = 0; i < 4; i++)
            refPixel[i] = camera.Project(new Vector3(refWorld[i], 0f));

        var world2pixel = Homography.Compute(refWorld, refPixel);
        var pixel2world = Homography.Compute(refPixel, refWorld);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var corner in new[] { new SKPoint(0, 0), new SKPoint(size, 0), new SKPoint(size, size), new SKPoint(0, size) })
        {
            var w = pixel2world.MapPoint(corner);
            minX = MathF.Min(minX, w.X); minY = MathF.Min(minY, w.Y);
            maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, w.Y);
        }

        float mx = (maxX - minX) * marginFraction, my = (maxY - minY) * marginFraction;
        return (world2pixel, new SKRect(minX - mx, minY - my, maxX + mx, maxY + my));
    }
}
