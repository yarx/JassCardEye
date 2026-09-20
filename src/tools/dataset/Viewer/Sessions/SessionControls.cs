using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>
/// A session from left to right, one column per frame: the card the model named in its colour - dimmed where
/// its confidence is below the threshold being tried - nothing as the dark ground, a white tick where the app
/// counted a card and a green one where the settings being tried would, and the anomalies in orange above.
/// Click or drag to show a frame.
/// </summary>
public sealed class SessionTimeline : Control
{
    private static readonly IBrush Ground = new SolidColorBrush(Color.Parse("#1b1b1b"));
    private static readonly IBrush AnomalyBrush = new SolidColorBrush(Color.Parse("#ff9f0a"));
    private static readonly IPen CommitPen = new Pen(Brushes.White, 2);
    private static readonly IPen ReplayPen = new Pen(new SolidColorBrush(Color.Parse("#30d158")), 2);
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#8cc4ff")), 2);

    private SessionAnalysis? _analysis;
    private double _threshold;
    private IReadOnlyList<CountedCard>? _replayed;
    private int _selected;
    private bool _dragging;

    /// <summary>The threshold being tried; frames below it are dimmed.</summary>
    internal double Threshold
    {
        get => _threshold;
        set { _threshold = value; InvalidateVisual(); }
    }

    /// <summary>Where the settings being tried would count a card.</summary>
    internal IReadOnlyList<CountedCard>? Replayed
    {
        get => _replayed;
        set { _replayed = value; InvalidateVisual(); }
    }

    public SessionTimeline()
    {
        Height = 72;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    internal SessionAnalysis? Analysis
    {
        get => _analysis;
        set { _analysis = value; InvalidateVisual(); }
    }

    internal int Selected
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>A frame (from 0) was clicked or dragged over.</summary>
    internal event Action<int>? FrameChosen;

    /// <summary>The same card always gets the same colour, in every session: its class ID around the colour wheel.</summary>
    internal static Color ColourOf(JassCardEye.Dataset.Cards.Card card) =>
        new HsvColor(1, card.ClassId * 137.508 % 360, 0.65, 0.85).ToRgb();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Ground, new Rect(Bounds.Size));
        if (_analysis is not { Frames.Count: > 0 } analysis) return;

        var frames = analysis.Frames;
        double column = Bounds.Width / frames.Count;
        // Runs of one card on one side of the threshold: a long session is a few rectangles, not one per frame.
        for (int start = 0; start < frames.Count;)
        {
            var card = frames[start].Card;
            bool below = frames[start].Confidence < _threshold;
            int end = start;
            while (end + 1 < frames.Count && frames[end + 1].Card == card && (frames[end + 1].Confidence < _threshold) == below) end++;
            if (card is { } named)
            {
                context.FillRectangle(new SolidColorBrush(ColourOf(named), below ? 0.22 : 1),
                    new Rect(start * column, 18, Math.Max(1, (end - start + 1) * column), 32));
            }
            start = end + 1;
        }
        foreach (var frame in frames)
        {
            if (!frame.Committed) continue;
            double x = (frame.Index + 0.5) * column;
            context.DrawLine(CommitPen, new Point(x, 53), new Point(x, 60));
        }
        foreach (var counted in _replayed ?? [])
        {
            double x = (counted.Frame + 0.5) * column;
            context.DrawLine(ReplayPen, new Point(x, 62), new Point(x, 70));
        }
        foreach (var anomaly in analysis.Anomalies)
        {
            context.FillRectangle(AnomalyBrush,
                new Rect(anomaly.Frame * column, 4, Math.Max(3, (anomaly.EndFrame - anomaly.Frame + 1) * column), 9));
        }
        double cursor = (_selected + 0.5) * column;
        context.DrawLine(CursorPen, new Point(cursor, 0), new Point(cursor, Bounds.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        Choose(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Choose(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
    }

    private void Choose(double x)
    {
        if (_analysis is not { Frames.Count: > 0 } analysis || Bounds.Width <= 0) return;
        int frame = Math.Clamp((int)(x / Bounds.Width * analysis.Frames.Count), 0, analysis.Frames.Count - 1);
        if (frame != _selected) FrameChosen?.Invoke(frame);
    }
}

/// <summary>A frame of a session, fitted into the space, with the box and the name the model gave it.</summary>
public sealed class SessionFrameView : Control
{
    private Bitmap? _image;
    private SessionFrame? _frame;

    internal void Show(Bitmap image, SessionFrame frame)
    {
        _image?.Dispose();
        _image = image;
        _frame = frame;
        InvalidateVisual();
    }

    internal void Clear()
    {
        _image?.Dispose();
        _image = null;
        _frame = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_image is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        double scale = Math.Min(Bounds.Width / _image.Size.Width, Bounds.Height / _image.Size.Height);
        var picture = new Rect((Bounds.Width - _image.Size.Width * scale) / 2, (Bounds.Height - _image.Size.Height * scale) / 2,
            _image.Size.Width * scale, _image.Size.Height * scale);
        context.DrawImage(_image, picture);

        if (_frame is not { Card: { } card } frame) return;
        var brush = new SolidColorBrush(SessionTimeline.ColourOf(card));
        var box = new Rect(picture.X + frame.Box.X * picture.Width, picture.Y + frame.Box.Y * picture.Height,
            frame.Box.Width * picture.Width, frame.Box.Height * picture.Height);
        using (context.PushClip(picture))
        {
            context.DrawRectangle(new Pen(brush, 3), box);
            var name = new FormattedText(
                string.Create(CultureInfo.InvariantCulture, $"{card.Display}  {frame.Confidence:P0}{(frame.Committed ? "  counted" : "")}"),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 15, Brushes.Black);
            var tag = new Rect(box.X, Math.Max(picture.Y, box.Y - name.Height - 6), name.Width + 10, name.Height + 4);
            context.FillRectangle(brush, tag);
            context.DrawText(name, new Point(tag.X + 5, tag.Y + 2));
        }
    }
}
