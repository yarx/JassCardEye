using System.Numerics;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Pose of a pinhole camera above the table. The table lies in the plane z=0 and the cards lie flat
/// on it. Different <see cref="Position"/>s simulate holding the camera over the table from varying
/// angles.
/// </summary>
/// <param name="Position">Camera position in world coordinates (above the table, z &gt; 0).</param>
/// <param name="Target">Look-at point on the table (usually near the origin).</param>
/// <param name="FovYDegrees">Vertical field of view in degrees.</param>
public readonly record struct CameraPose(Vector3 Position, Vector3 Target, float FovYDegrees);

/// <summary>
/// Placement of a card lying flat on the table: centre in the table plane, rotation within that
/// plane, and a small height above the table (stack thickness). The card always stays parallel to
/// the table plane – <see cref="Height"/> only lifts it, it does not tilt it.
/// </summary>
public readonly record struct CardPlacement(Card Card, Vector2 Center, float RotationDegrees, float Height = 0f);

/// <summary>
/// Description of the background. Either an image (<see cref="ImagePath"/>) or, if none is set, a
/// procedurally generated Jass mat with the given seed.
/// </summary>
public readonly record struct BackgroundSpec(string? ImagePath, int ProceduralSeed);

/// <summary>
/// Scene lighting as a point light above the table. Creates a brighter area around
/// <see cref="GroundCenter"/> with falloff outward (on the table plane, hence perspectively
/// consistent with the camera) as well as soft contact shadows under the cards.
/// </summary>
/// <param name="GroundCenter">Brightest point on the table plane (world x/y).</param>
/// <param name="Radius">Falloff radius of the light in world units.</param>
/// <param name="Ambient">Residual brightness at the edge (0..1); 1 = no falloff.</param>
/// <param name="ShadowStrength">Strength of the card shadows (0..1).</param>
/// <param name="FillCenter">Centre of an optional second, purely brightening source.</param>
/// <param name="FillRadius">Falloff radius of that second source.</param>
/// <param name="FillStrength">Its strength (0 = no second source).</param>
/// <param name="ShadowSoftness">
/// 0 = hard, short shadow (a single lamp), 1 = wide, faint shadow (overcast or diffuse room light).
/// </param>
public readonly record struct SceneLight(
    Vector2 GroundCenter,
    float Radius,
    float Ambient,
    float ShadowStrength,
    Vector2 FillCenter = default,
    float FillRadius = 0f,
    float FillStrength = 0f,
    float ShadowSoftness = 0.35f);

/// <summary>
/// The knobs of a scene. <c>null</c> means "choose randomly via the <see cref="SceneRandomizer"/>";
/// a set value is taken as is, so the same path serves random data and a deliberately fixed scene.
/// </summary>
public sealed record GenerationParameters
{
    /// <summary>Topmost (target) card, or <c>null</c> for random.</summary>
    public Card? TopCard { get; init; }

    /// <summary>Number of underlying cards (0..35), or <c>null</c> for random.</summary>
    public int? UnderCardCount { get; init; }

    /// <summary>Camera pose, or <c>null</c> for random.</summary>
    public CameraPose? Camera { get; init; }

    /// <summary>
    /// Distance of the camera to the look-at point in world units (smaller = closer), or <c>null</c>
    /// for random. Simulates how close a person holds the camera over the cards. At near distances,
    /// widely scattered cards extend beyond the image; the topmost card is only labelled when it lies
    /// completely inside the frame. Ignored when <see cref="Camera"/> is fully set.
    /// </summary>
    public float? CameraDistance { get; init; }

    /// <summary>
    /// <c>true</c> renders the pile as a squared deck instead of a scattered heap: all cards flush on
    /// top of each other, so only the topmost one shows its face and the stack's edges form a band
    /// below it. Rare in play - cards are usually thrown onto the pile - but it does occur.
    /// <c>null</c>/<c>false</c> keeps the scattered heap.
    /// </summary>
    public bool? AlignedStack { get; init; }

    /// <summary>Exposure factor (1 = neutral), or <c>null</c> for random.</summary>
    public float? Exposure { get; init; }

    /// <summary>Lighting, or <c>null</c> for random.</summary>
    public SceneLight? Light { get; init; }

    /// <summary>Background, or <c>null</c> for random.</summary>
    public BackgroundSpec? Background { get; init; }

    /// <summary>Edge length of the square output image in pixels.</summary>
    public int ImageSize { get; init; } = 640;
}

/// <summary>
/// A card moving through the frame while the shutter is open, drawn motion-blurred on top of
/// everything else. Models a card being thrown onto the pile. <see cref="Motion"/> is the travel
/// during the exposure as a multiple of the card's own projected height, so the blur scales with how
/// large the card appears.
/// </summary>
public readonly record struct FlyingCard(CardPlacement Placement, Vector2 Motion);

/// <summary>
/// Fully resolved scene, ready to render. The card list is ordered bottom to top; the last element
/// is the topmost card (the detection target). An empty card list is a background-only scene
/// (negative sample). <see cref="TopCardMotion"/> smears the topmost card itself (a card thrown in
/// mid-motion – also a negative). <see cref="Flying"/> adds a separate blurred card passing over the
/// scene; whether that still leaves a usable top card is decided by the generator.
///
/// <see cref="Deck"/> holds for the whole scene: every card in <see cref="Cards"/> and the
/// <see cref="Flying"/> one come from it. It stays on the scene even when the card list is emptied
/// for a negative, so a scene never has to be asked which deck it *was*.
/// </summary>
public sealed record Scene(
    IReadOnlyList<CardPlacement> Cards,
    CameraPose Camera,
    float Exposure,
    SceneLight Light,
    BackgroundSpec Background,
    int ImageSize,
    Deck Deck,
    Vector2 TopCardMotion = default,
    FlyingCard? Flying = null,
    bool AlignedStack = false)
{
    /// <summary>The topmost card – the label refers solely to it. Only valid when cards are present.</summary>
    public CardPlacement TopCard => Cards[^1];
}

/// <summary>
/// World dimensions of a card and computation of its corner points. The proportion matches the real
/// scans (1710×2670 ≈ 0.64) resp. a real Jass card (57×89 mm).
/// </summary>
public static class CardGeometry
{
    /// <summary>Card height in world units (reference measure).</summary>
    public const float WorldHeight = 1.0f;

    /// <summary>Card width in world units (height × aspect ratio).</summary>
    public const float WorldWidth = 0.64f;

    /// <summary>Real card height in millimetres – reference for the mm ↔ world conversion.</summary>
    public const float CardHeightMillimeters = 89f;

    /// <summary>Converts a measure in millimetres to world units.</summary>
    public static float MillimetersToWorld(float millimeters) =>
        millimeters / CardHeightMillimeters * WorldHeight;

    /// <summary>Projects the four corners of a placed card to pixel coordinates.</summary>
    public static Vector2[] ProjectCorners(CardPlacement placement, PinholeCamera camera)
    {
        var world = WorldCorners(placement);
        var pixels = new Vector2[4];
        for (int i = 0; i < 4; i++)
            pixels[i] = camera.Project(world[i]);
        return pixels;
    }

    /// <summary>Height of a projected card in pixels (mid top edge to mid bottom edge).</summary>
    public static float ProjectedHeight(Vector2[] corners)
    {
        var top = (corners[0] + corners[1]) * 0.5f;
        var bottom = (corners[3] + corners[2]) * 0.5f;
        return (bottom - top).Length();
    }

    /// <summary>
    /// The four corner points of the placed card in world coordinates (z is the placement's height
    /// above the table), in the order top-left, top-right, bottom-right, bottom-left – matching the
    /// pixel corner order of the card image so the texture is not mirrored.
    /// </summary>
    public static Vector3[] WorldCorners(CardPlacement placement)
    {
        float hw = WorldWidth / 2f;
        float hh = WorldHeight / 2f;

        // Local corners; +Y is the top edge of the card.
        Span<Vector2> local =
        [
            new(-hw, +hh), // top-left
            new(+hw, +hh), // top-right
            new(+hw, -hh), // bottom-right
            new(-hw, -hh), // bottom-left
        ];

        float rad = placement.RotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        var corners = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float x = local[i].X * cos - local[i].Y * sin + placement.Center.X;
            float y = local[i].X * sin + local[i].Y * cos + placement.Center.Y;
            corners[i] = new Vector3(x, y, placement.Height);
        }
        return corners;
    }
}
