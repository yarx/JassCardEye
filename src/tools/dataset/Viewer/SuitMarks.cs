using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JassCardEye.Dataset.Cards;
using SkiaSharp;
using Svg.Skia;

namespace JassCardEye.Dataset.Viewer;

/// <summary>
/// The suit drawings of the iOS app, as bitmaps for the class picker.
///
/// The SVGs in Assets/Suits are copies of the app's asset catalogue, so the viewer shows the marks the
/// app shows. They are rendered once per suit and kept, because the picker rebuilds its buttons on
/// every deck switch.
/// </summary>
internal static class SuitMarks
{
    // Rendered larger than the 18 px the buttons show, so the marks stay sharp on a Retina screen
    // and in the 2x snapshots.
    private const int Pixels = 72;

    private static readonly Dictionary<Suit, Bitmap> Cache = [];

    public static Bitmap For(Suit suit)
    {
        if (Cache.TryGetValue(suit, out var cached)) return cached;

        // The files are named after the suit tokens (acorns, bells, ...), as in the app's catalogue
        // and in the labels - one name for the suit everywhere.
        var uri = new Uri($"avares://JassCardEye.Dataset.Viewer/Assets/Suits/{suit.ToToken()}.svg");
        using var stream = AssetLoader.Open(uri);
        using var svg = new SKSvg();
        var picture = svg.Load(stream)
            ?? throw new InvalidOperationException($"The suit drawing {uri} could not be read.");

        using var surface = SKSurface.Create(new SKImageInfo(Pixels, Pixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Fit the drawing into the square, keeping its proportions: the German marks are drawn on a
        // square, Kreuz carries its own margin in the view box, and neither may be stretched.
        var bounds = picture.CullRect;
        float scale = Pixels / Math.Max(bounds.Width, bounds.Height);
        canvas.Translate((Pixels - bounds.Width * scale) / 2, (Pixels - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bitmap = new Bitmap(png.AsStream());
        Cache[suit] = bitmap;
        return bitmap;
    }
}
