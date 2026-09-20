using System.Collections.Concurrent;
using JassCardEye.Dataset.Cards;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Loads the real card scans from <c>{root}/{deck}/{suit}/{rank}.jpg</c> (matches the layout of
/// data/cards/) and caches them as <see cref="SKImage"/>. Safe to share across threads: the cache is
/// concurrent and each entry is created exactly once, and an <see cref="SKImage"/> is immutable once
/// decoded.
/// </summary>
public sealed class CardImageSource(string cardsRoot) : IDisposable
{
    // Corner radius as a fraction of the shorter image edge (≈ 4.6 mm at 57 mm card width).
    private const float CornerRadiusFraction = 0.08f;

    private readonly string _cardsRoot = cardsRoot;
    private readonly ConcurrentDictionary<Card, Lazy<SKImage>> _cache = new();

    /// <summary>
    /// Decodes all 72 cards up front. Worth doing before parallel rendering: the scans are large, so
    /// decoding them inside the parallel loop would just make the threads queue up on the same files.
    /// Both decks are loaded even though a single image only ever uses one of them - a run covers
    /// both, and the alternative is every thread racing to decode the same file mid-run.
    /// </summary>
    public void Preload()
    {
        foreach (var card in JassDeck.Cards)
            Load(card);
    }

    public SKImage Load(Card card) =>
        // Lazy guarantees the factory runs once per card even under concurrent access.
        _cache.GetOrAdd(card, c => new Lazy<SKImage>(() => Decode(c))).Value;

    private SKImage Decode(Card card)
    {
        string path = Path.Combine(
            _cardsRoot, card.Deck.ToToken(), card.Suit.ToToken(), card.Rank.ToToken() + ".jpg");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Card image missing: {path}", path);

        using var data = SKData.Create(path);
        using var raw = SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException($"Card image could not be decoded: {path}");

        return RoundCorners(raw);
    }

    // Rounds the corners by drawing the image into a rounded-rectangle mask. The corners thus become
    // transparent – when warped later they reveal the card underneath.
    private static SKImage RoundCorners(SKImage source)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float radius = CornerRadiusFraction * MathF.Min(source.Width, source.Height);
        using var rrect = new SKRoundRect(new SKRect(0, 0, source.Width, source.Height), radius, radius);
        canvas.ClipRoundRect(rrect, antialias: true);
        canvas.DrawImage(source, 0, 0);

        return surface.Snapshot();
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
            if (entry.IsValueCreated)
                entry.Value.Dispose();
        _cache.Clear();
    }
}
