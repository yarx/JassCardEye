using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JassCardEye.Dataset.Cards;
using JassCardEye.Dataset.Generation;
using SkiaSharp;

namespace JassCardEye.Dataset.Viewer.Simulate;

/// <summary>
/// A frame with its label drawn on: the top card's outline in the colour of what the frame is - green a
/// positive, red a negative, amber the ambiguous band a dataset leaves out.
/// </summary>
public sealed class SimulationFrameView : Control
{
    private Bitmap? _image;
    private Vector2[]? _corners;
    private string? _kind;
    private string? _caption;
    private int _size = 640;

    internal void Show(Bitmap image, Vector2[]? corners, string kind, string caption, int size)
    {
        _image?.Dispose();
        (_image, _corners, _kind, _caption, _size) = (image, corners, kind, caption, size);
        InvalidateVisual();
    }

    internal void Clear()
    {
        _image?.Dispose();
        _image = null;
        _corners = null;
        InvalidateVisual();
    }

    internal static Color ColourOf(string kind) => kind switch
    {
        "positive" => Color.Parse("#3ddc84"),
        "negative" => Color.Parse("#ff5c5c"),
        _ => Color.Parse("#ffb020"),
    };

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_image is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        double scale = Math.Min(Bounds.Width / _image.Size.Width, Bounds.Height / _image.Size.Height);
        var picture = new Rect((Bounds.Width - _image.Size.Width * scale) / 2, (Bounds.Height - _image.Size.Height * scale) / 2,
            _image.Size.Width * scale, _image.Size.Height * scale);
        context.DrawImage(_image, picture);
        var brush = new SolidColorBrush(ColourOf(_kind ?? ""));

        using (context.PushClip(picture))
        {
            if (_corners is { Length: 4 })
            {
                double k = picture.Width / _size;
                var points = Array.ConvertAll(_corners, c => new Point(picture.X + c.X * k, picture.Y + c.Y * k));
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(points[0], isFilled: false);
                    for (int i = 1; i < 4; i++) g.LineTo(points[i]);
                    g.EndFigure(isClosed: true);
                }
                context.DrawGeometry(null, new Pen(brush, 3), geometry);
                context.DrawEllipse(brush, null, points[0], 5, 5);   // the first corner: the scan's top left
            }
            if (_caption is { Length: > 0 })
            {
                var text = new FormattedText(_caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 14, Brushes.Black);
                var tag = new Rect(picture.X + 8, picture.Y + 8, text.Width + 12, text.Height + 6);
                context.FillRectangle(brush, tag);
                context.DrawText(text, new Point(tag.X + 6, tag.Y + 3));
            }
        }
    }
}

/// <summary>
/// The 2D generator's picture of the same deck, seed and pile - what the training data looks like today,
/// next to the 3D frame. Rendered in process with the generator's own classes, from the camera tilt the 3D
/// scene drew, so the two differ in how they were made and not in what they show.
/// </summary>
internal static class Counterpart2D
{
    public static (Bitmap Image, Vector2[] Corners, string Card) Render(string cardsRoot, string deck, int seed, int played,
        double tiltDegrees, int size)
    {
        using var cards = new CardImageSource(cardsRoot);
        var backgrounds = new BackgroundSource(null);
        var randomizer = new SceneRandomizer(new RandomizerOptions(), backgrounds);
        var plan = new ScenePlan(deck == "german" ? Deck.German : Deck.French,
            (float)Math.Clamp(tiltDegrees, RunPlan.MinTiltDegrees, RunPlan.MaxTiltDegrees));
        var scene = randomizer.Resolve(new GenerationParameters { ImageSize = size, UnderCardCount = played }, plan, new Random(seed));
        var renderer = new SceneRenderer(cards, backgrounds);
        using var result = renderer.Render(scene);
        using var data = result.Image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return (new Bitmap(stream), result.TopCardCorners, result.Scene.TopCard.Card.Label);
    }
}
