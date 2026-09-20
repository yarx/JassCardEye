using System.Numerics;
using JassCardEye.Dataset.Cards;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Resolves <see cref="GenerationParameters"/> into a complete <see cref="Scene"/>. Parameters that
/// are not set (<c>null</c>) are drawn deterministically from the given <see cref="Random"/>, so a
/// seed yields reproducible datasets.
/// </summary>
public sealed class SceneRandomizer(RandomizerOptions options, BackgroundSource backgrounds)
{
    private readonly RandomizerOptions _options = options;
    private readonly BackgroundSource _backgrounds = backgrounds;

    public Scene Resolve(GenerationParameters parameters, ScenePlan plan, Random rng)
    {
        // Everything on the table comes from one deck - the pile, and later the flying card too.
        // A Jass is played with one deck or the other, so a mixed heap is a scene that cannot occur.
        var deck = JassDeck.Of(plan.Deck);
        var topCard = parameters.TopCard ?? RandomCard(deck, rng);

        int underCount = parameters.UnderCardCount
                         ?? rng.Next(_options.MinUnderCards, _options.MaxUnderCards + 1);
        underCount = Math.Clamp(underCount, 0, JassDeck.CardsPerDeck - 1);

        var cards = new List<CardPlacement>(underCount + 1);

        // Each layer sits one card thickness higher than the one below – the cards stay parallel to
        // the table but gain a small stack height.
        float thickness = CardGeometry.MillimetersToWorld(_options.CardThicknessMillimeters);

        // Underlying cards: distinct cards from the rest of the deck (without the topmost), randomly
        // scattered. So the maximum case is exactly one full 36-card deck.
        var underCards = deck
            .Where(c => c != topCard)
            .OrderBy(_ => rng.Next())
            .Take(underCount)
            .ToList();

        for (int layer = 0; layer < underCards.Count; layer++)
        {
            cards.Add(new CardPlacement(
                underCards[layer],
                RandomPointInDisc(rng, _options.UnderCardRadius),
                (float)(rng.NextDouble() * 360.0),
                layer * thickness));
        }

        // Topmost card last and on top (drawn over all others).
        cards.Add(new CardPlacement(
            topCard,
            RandomPointInDisc(rng, _options.TopCardJitter),
            (float)(rng.NextDouble() * 360.0),
            underCards.Count * thickness));

        bool aligned = parameters.AlignedStack == true;
        if (aligned)
            AlignStack(cards);

        var camera = parameters.Camera ?? RandomCamera(rng, parameters.CameraDistance, plan.TiltDegrees);
        float exposure = parameters.Exposure ?? Lerp(_options.MinExposure, _options.MaxExposure, rng);
        var light = parameters.Light ?? RandomLight(rng);
        var background = parameters.Background ?? RandomBackground(rng);

        return new Scene(cards, camera, exposure, light, background, parameters.ImageSize, plan.Deck,
            AlignedStack: aligned);
    }

    /// <summary>
    /// Turns an already-drawn scattered heap into a squared deck, in place.
    ///
    /// Deliberately consumes no further random numbers: it rescales the offsets and rotations that
    /// were drawn for the heap. The random stream therefore advances exactly as it would without the
    /// stack, so every frame that is *not* marked as a stack renders bit-identically to a run with the
    /// same seed and count and no squared decks - the stacks are an added tail, not a shifted
    /// distribution.
    /// </summary>
    private void AlignStack(List<CardPlacement> cards)
    {
        if (cards.Count == 0) return;

        var top = cards[^1];
        float thickness = CardGeometry.MillimetersToWorld(_options.CardThicknessMillimeters);
        float shrink = _options.UnderCardRadius > 0
            ? _options.AlignedStackRadius / _options.UnderCardRadius
            : 0f;

        for (int layer = 0; layer < cards.Count - 1; layer++)
        {
            var card = cards[layer];
            // The drawn offset lies in a disc of UnderCardRadius; shrinking it keeps the per-card
            // variation but collapses the heap onto the deck's centre.
            cards[layer] = card with
            {
                Center = top.Center + card.Center * shrink,
                RotationDegrees = top.RotationDegrees
                    + (card.RotationDegrees / 360f - 0.5f) * 2f * _options.AlignedStackRotationDegrees,
                Height = layer * thickness,
            };
        }

        cards[^1] = top with { Height = (cards.Count - 1) * thickness };
    }

    /// <summary>
    /// Adds a motion-blurred card flying over the scene and reports whether the top card stays
    /// usable. With <paramref name="occluding"/> the flying card is placed so that it covers a
    /// substantial part of the top card (→ the frame becomes a negative); otherwise it only clips in
    /// from the border and leaves the top card clearly visible (→ the frame stays a positive).
    /// Returns <c>null</c> when no placement satisfying the requested case was found.
    /// </summary>
    public Scene? TryAddFlyingCard(Scene scene, Random rng, bool occluding)
    {
        if (scene.Cards.Count == 0) return null;

        int size = scene.ImageSize;
        var camera = new PinholeCamera(scene.Camera, size);
        var topBox = BoundsOf(CardGeometry.ProjectCorners(scene.TopCard, camera));
        float topArea = Area(topBox);
        if (topArea <= 0) return null;

        var (_, worldBounds) = PlaneProjection.ForCamera(camera, size);
        var imageBox = (Left: 0f, Top: 0f, Right: (float)size, Bottom: (float)size);
        var topCentre = scene.TopCard.Center;

        // How much of the flying card should end up in frame. For the border case this alternates
        // between a narrow sliver and a larger portion, so the dataset covers both.
        var (minVisible, maxVisible, minShare) = occluding
            ? (0.35f, 0.97f, 0.10f)
            : rng.NextDouble() < 0.5
                ? (0.05f, 0.35f, 0.015f)
                : (0.30f, 0.70f, 0.050f);

        for (int attempt = 0; attempt < 60; attempt++)
        {
            // From the scene's own deck: a card sailing over a German pile is a German card.
            var card = JassDeck.Of(scene.Deck)[rng.Next(JassDeck.CardsPerDeck)];
            if (card == scene.TopCard.Card) continue;

            // Occluding: right over the pile. Border: somewhere along the edge of the visible table,
            // pushed outwards so the card is only partly in frame.
            var centre = occluding
                ? topCentre + RandomPointInDisc(rng, 0.45f)
                : RandomPointOnBorder(rng, worldBounds);

            // Well above the table, so the card appears close to the camera and correspondingly large.
            float height = 0.25f + (float)rng.NextDouble() * 0.65f;
            var placement = new CardPlacement(card, centre, (float)(rng.NextDouble() * 360.0), height);
            var flyBox = BoundsOf(CardGeometry.ProjectCorners(placement, camera));
            float flyArea = Area(flyBox);
            if (flyArea <= 0) continue;

            float inFrame = IntersectArea(flyBox, imageBox);
            float visible = inFrame / flyArea;                 // how much of the card is in frame
            float screenShare = inFrame / (size * (float)size); // how much of the frame it takes up
            float overlap = IntersectArea(flyBox, topBox) / topArea;

            bool inBand = visible >= minVisible && visible <= maxVisible && screenShare >= minShare;
            bool ok = occluding
                ? inBand && overlap >= 0.30f   // covers a substantial part of the top card
                : inBand && overlap <= 0.05f;  // clips in from the border, well clear of the top card
            if (!ok) continue;

            // A passing card is blurred to varying degrees; one clipping in at the border may be
            // almost sharp, since it does not affect whether the top card can be read.
            var motion = occluding
                ? RandomMotion(rng, 0.12f, 0.40f)
                : RandomMotion(rng, 0.04f, 0.30f);
            return scene with { Flying = new FlyingCard(placement, motion) };
        }
        return null;
    }

    /// <summary>
    /// Travel during the exposure in a random direction, as a multiple of the card's own projected
    /// height.
    /// </summary>
    public static Vector2 RandomMotion(Random rng, float minLength, float maxLength)
    {
        float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
        float length = minLength + (float)rng.NextDouble() * (maxLength - minLength);
        return new Vector2(MathF.Cos(angle) * length, MathF.Sin(angle) * length);
    }

    private static Vector2 RandomPointOnBorder(Random rng, SKRect bounds)
    {
        float outward = 0.15f + (float)rng.NextDouble() * 0.45f;
        return rng.Next(4) switch
        {
            0 => new Vector2(Lerp(bounds.Left, bounds.Right, rng), bounds.Top - outward),
            1 => new Vector2(Lerp(bounds.Left, bounds.Right, rng), bounds.Bottom + outward),
            2 => new Vector2(bounds.Left - outward, Lerp(bounds.Top, bounds.Bottom, rng)),
            _ => new Vector2(bounds.Right + outward, Lerp(bounds.Top, bounds.Bottom, rng)),
        };
    }

    private static (float Left, float Top, float Right, float Bottom) BoundsOf(Vector2[] corners)
    {
        float minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X);
        float minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
        return (minX, minY, maxX, maxY);
    }

    private static float Area((float Left, float Top, float Right, float Bottom) b) =>
        MathF.Max(0, b.Right - b.Left) * MathF.Max(0, b.Bottom - b.Top);

    private static float IntersectArea(
        (float Left, float Top, float Right, float Bottom) a,
        (float Left, float Top, float Right, float Bottom) b) =>
        MathF.Max(0, MathF.Min(a.Right, b.Right) - MathF.Max(a.Left, b.Left)) *
        MathF.Max(0, MathF.Min(a.Bottom, b.Bottom) - MathF.Max(a.Top, b.Top));

    /// <summary>
    /// Draws a lighting situation. Two moods, because a table is lit in two ways in practice: by a
    /// single lamp, which puts a pool of light somewhere and casts a clear shadow, or diffusely by an
    /// overcast sky or a bright room, where the light is almost even and the shadow is only a soft
    /// darkening under the card. A lamp may get a second, weaker source from another direction.
    ///
    /// The light centre is drawn from a disc large enough to fall beside, behind or outside the
    /// frame, so the illumination is not systematically frontal.
    /// </summary>
    private SceneLight RandomLight(Random rng)
    {
        var center = RandomPointInDisc(rng, _options.LightOffsetRadius);
        bool diffuse = rng.NextDouble() < _options.DiffuseLightProbability;

        if (diffuse)
        {
            return new SceneLight(
                center,
                Lerp(_options.MinDiffuseRadius, _options.MaxDiffuseRadius, rng),
                Lerp(_options.MinDiffuseAmbient, _options.MaxDiffuseAmbient, rng),
                Lerp(_options.MinShadowStrength, _options.MaxShadowStrength, rng) * 0.55f,
                ShadowSoftness: 0.7f + (float)rng.NextDouble() * 0.3f);
        }

        var light = new SceneLight(
            center,
            Lerp(_options.MinLightRadius, _options.MaxLightRadius, rng),
            Lerp(_options.MinAmbient, _options.MaxAmbient, rng),
            Lerp(_options.MinShadowStrength, _options.MaxShadowStrength, rng),
            ShadowSoftness: (float)rng.NextDouble() * 0.45f);

        if (rng.NextDouble() >= _options.FillLightProbability)
            return light;

        // Second source, roughly opposite the first so the two come from different sides.
        var opposite = -center + RandomPointInDisc(rng, _options.LightOffsetRadius * 0.5f);
        return light with
        {
            FillCenter = opposite,
            FillRadius = Lerp(_options.MinLightRadius, _options.MaxDiffuseRadius, rng),
            FillStrength = 0.25f + (float)rng.NextDouble() * 0.45f,
        };
    }

    private static Card RandomCard(IReadOnlyList<Card> deck, Random rng) => deck[rng.Next(deck.Count)];

    private CameraPose RandomCamera(Random rng, float? distanceOverride, float tiltDegrees)
    {
        float azimuth = (float)(rng.NextDouble() * 2.0 * Math.PI);
        float tilt = Deg2Rad(tiltDegrees);
        float sampledDistance = Lerp(_options.MinDistance, _options.MaxDistance, rng);
        float distance = distanceOverride ?? sampledDistance;
        float fov = Lerp(_options.MinFovDegrees, _options.MaxFovDegrees, rng);

        var target = new Vector3(
            (float)((rng.NextDouble() * 2 - 1) * _options.TargetJitter),
            (float)((rng.NextDouble() * 2 - 1) * _options.TargetJitter),
            0f);

        // Direction from the look-at point to the camera: tilt from vertical (z), rotated by azimuth.
        var direction = new Vector3(
            MathF.Sin(tilt) * MathF.Cos(azimuth),
            MathF.Sin(tilt) * MathF.Sin(azimuth),
            MathF.Cos(tilt));

        var position = target + distance * direction;
        return new CameraPose(position, target, fov);
    }

    private BackgroundSpec RandomBackground(Random rng)
    {
        int seed = rng.Next();
        if (_backgrounds.HasImages && rng.NextDouble() < _options.BackgroundImageProbability)
            return new BackgroundSpec(_backgrounds.PickImage(seed), seed);

        return new BackgroundSpec(null, seed);
    }

    private static Vector2 RandomPointInDisc(Random rng, float radius)
    {
        // Uniformly distributed in the disc (sqrt for an area-uniform distribution).
        float r = radius * MathF.Sqrt((float)rng.NextDouble());
        float a = (float)(rng.NextDouble() * 2.0 * Math.PI);
        return new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
    }

    private static float Lerp(float min, float max, Random rng) => min + (max - min) * (float)rng.NextDouble();

    private static float Deg2Rad(float deg) => deg * MathF.PI / 180f;
}
