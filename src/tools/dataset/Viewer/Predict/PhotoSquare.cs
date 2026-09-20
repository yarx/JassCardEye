using System;
using System.Numerics;

namespace JassCardEye.Dataset.Viewer.Predict;

/// <summary>
/// The central square of a photo: the picture the dataset stores, and therefore the only picture a
/// model trained on this dataset has ever seen (<c>VariantDatasetWriter.CenterCropSquare</c>).
///
/// Labelling happens on the photo, everything else on that square, so both directions of this one
/// mapping are needed: a stored label is drawn back onto the photo, and a proposal comes back from
/// the models in the square's pixels. One place for it, because a photo that is not square would
/// otherwise be labelled slightly beside the card in two different ways.
/// </summary>
internal readonly record struct PhotoSquare(float OffsetX, float OffsetY, float Scale)
{
    /// <summary>
    /// The square of a <paramref name="width"/>×<paramref name="height"/> photo, as an image of
    /// <paramref name="size"/> pixels a side.
    /// </summary>
    public static PhotoSquare Of(int width, int height, int size)
    {
        int side = Math.Min(width, height);
        if (side <= 0 || size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "a square has a side");
        return new PhotoSquare((width - side) / 2f, (height - side) / 2f, size / (float)side);
    }

    /// <summary>Where a point of the square lies in the photo.</summary>
    public Vector2 ToPhoto(Vector2 point) => new(OffsetX + point.X / Scale, OffsetY + point.Y / Scale);
}
