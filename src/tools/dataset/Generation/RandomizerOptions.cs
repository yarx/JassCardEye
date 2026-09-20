namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Value ranges from which the <see cref="SceneRandomizer"/> draws random scenes. The defaults yield
/// plausible "camera over the table" shots; every value can be overridden.
/// </summary>
public sealed record RandomizerOptions
{
    /// <summary>Minimum number of underlying cards.</summary>
    public int MinUnderCards { get; init; } = 0;

    /// <summary>Maximum number of underlying cards (deck without the topmost card = 35).</summary>
    public int MaxUnderCards { get; init; } = 35;

    /// <summary>Radius (world units) within which underlying cards scatter around the centre.</summary>
    public float UnderCardRadius { get; init; } = 0.55f;

    /// <summary>
    /// Maximum offset of the topmost card from the table centre. Deliberately larger so the topmost
    /// card does not systematically lie in the centre (otherwise pure classification learns "centre =
    /// answer"). A visibility guarantee in the generator rejects cases that are cropped too heavily.
    /// </summary>
    public float TopCardJitter { get; init; } = 0.32f;

    /// <summary>
    /// Thickness of a card in millimetres - the real measure of a Jass card. Each card sits this much
    /// higher than the one below, so a pile visibly gains height: a full deck is about a centimetre
    /// tall, which shows as parallax on a heap and as a band of cut edges on a squared deck.
    /// </summary>
    public float CardThicknessMillimeters { get; init; } = 0.3f;

    /// <summary>Residual scatter of a squared deck - hand-stacked cards are never perfectly flush.</summary>
    public float AlignedStackRadius { get; init; } = 0.014f;

    /// <summary>Residual rotation of a squared deck in degrees, applied around the deck's own angle.</summary>
    public float AlignedStackRotationDegrees { get; init; } = 2.5f;

    /// <summary>Minimum camera distance to the look-at point (near – cards may extend beyond the image).</summary>
    public float MinDistance { get; init; } = 1.8f;

    /// <summary>Maximum camera distance to the look-at point (far – whole stack visible with margin).</summary>
    public float MaxDistance { get; init; } = 4.2f;

    /// <summary>Minimum vertical field of view in degrees.</summary>
    public float MinFovDegrees { get; init; } = 45f;

    /// <summary>Maximum vertical field of view in degrees.</summary>
    public float MaxFovDegrees { get; init; } = 55f;

    /// <summary>Maximum offset of the look-at point from the origin.</summary>
    public float TargetJitter { get; init; } = 0.08f;

    /// <summary>Minimum exposure factor (darker).</summary>
    public float MinExposure { get; init; } = 0.8f;

    /// <summary>Maximum exposure factor (brighter).</summary>
    public float MaxExposure { get; init; } = 1.2f;

    /// <summary>
    /// Maximum offset of the light centre from the table centre (world units). Large enough for the
    /// bright spot to sit beside the pile, behind it, or outside the frame entirely - with a small
    /// offset the light would always sit where the camera looks, which reads as a lamp mounted on
    /// the camera and gives every image the same frontal lighting.
    /// </summary>
    public float LightOffsetRadius { get; init; } = 2.6f;

    /// <summary>Share of scenes lit diffusely (overcast, large room) instead of by a single lamp.</summary>
    public double DiffuseLightProbability { get; init; } = 0.35;

    /// <summary>Share of lamp-lit scenes that get a second, brightening source from another side.</summary>
    public double FillLightProbability { get; init; } = 0.3;

    /// <summary>Minimum light falloff radius (world units) - a small pool of light.</summary>
    public float MinLightRadius { get; init; } = 1.4f;

    /// <summary>Maximum light falloff radius (world units).</summary>
    public float MaxLightRadius { get; init; } = 3.6f;

    /// <summary>Falloff radius range for diffuse light: wide enough to cover the whole visible table.</summary>
    public float MinDiffuseRadius { get; init; } = 4.5f;

    /// <summary>Upper end of the diffuse falloff radius.</summary>
    public float MaxDiffuseRadius { get; init; } = 8f;

    /// <summary>Minimum residual brightness at the edge (0..1) under a lamp.</summary>
    public float MinAmbient { get; init; } = 0.45f;

    /// <summary>Maximum residual brightness at the edge (0..1) under a lamp.</summary>
    public float MaxAmbient { get; init; } = 0.78f;

    /// <summary>Residual brightness range for diffuse light - near 1 means practically even light.</summary>
    public float MinDiffuseAmbient { get; init; } = 0.82f;

    /// <summary>Upper end of the diffuse residual brightness.</summary>
    public float MaxDiffuseAmbient { get; init; } = 0.97f;

    /// <summary>Minimum card shadow strength (0..1).</summary>
    public float MinShadowStrength { get; init; } = 0.3f;

    /// <summary>Maximum card shadow strength (0..1).</summary>
    public float MaxShadowStrength { get; init; } = 0.55f;

    /// <summary>Probability of using a background image, if any are present.</summary>
    public double BackgroundImageProbability { get; init; } = 0.85;
}
