using System.Globalization;
using System.Numerics;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Axis-aligned YOLO bounding box in the standard format <c>classId cx cy w h</c> with values
/// normalised to the image edge lengths (0..1).
/// </summary>
public readonly record struct YoloBox(int ClassId, float CenterX, float CenterY, float Width, float Height)
{
    /// <summary>
    /// Enclosing axis-aligned box around the four (projected) corner points of a card, normalised to
    /// image width/height and clipped to the image. Separate width/height so non-square images (real
    /// photos) are normalised correctly too.
    /// </summary>
    public static YoloBox FromCorners(int classId, Vector2[] corners, int width, int height)
    {
        float minX = width, minY = height, maxX = 0, maxY = 0;
        foreach (var c in corners)
        {
            minX = MathF.Min(minX, c.X);
            minY = MathF.Min(minY, c.Y);
            maxX = MathF.Max(maxX, c.X);
            maxY = MathF.Max(maxY, c.Y);
        }

        minX = Math.Clamp(minX, 0, width);
        maxX = Math.Clamp(maxX, 0, width);
        minY = Math.Clamp(minY, 0, height);
        maxY = Math.Clamp(maxY, 0, height);

        float cx = (minX + maxX) / 2f / width;
        float cy = (minY + maxY) / 2f / height;
        float w = (maxX - minX) / width;
        float h = (maxY - minY) / height;
        return new YoloBox(classId, cx, cy, w, h);
    }

    /// <summary>A single YOLO label line (invariant-formatted, without a trailing newline).</summary>
    public string ToLine() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000}",
        ClassId, CenterX, CenterY, Width, Height);
}
