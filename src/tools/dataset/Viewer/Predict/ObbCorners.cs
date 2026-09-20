using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace JassCardEye.Dataset.Viewer.Predict;

/// <summary>
/// Turns the four corners of an oriented box into the order a label is written in: top left, top
/// right, bottom right, bottom left of the card standing upright.
///
/// B₁ answers with the corners in order around the box, but it does not say where that order starts,
/// which way round it runs, or which end of the card is its head - an oriented box is symmetric under
/// half a turn and the model has no opinion about it. Three things follow, and all three matter,
/// because <c>CardCrop.Rectify</c> maps the first corner to the top left of the crop:
///
/// - the winding decides whether the rectified card comes out mirrored,
/// - the start decides whether it comes out upright or lying on its side,
/// - the remaining half turn decides whether it comes out on its head.
///
/// The third one is decided here, by which way up the card stands on the photo, and **not** by asking
/// B₂ about both crops. On real photos B₂ scores a crop and the same crop turned by 180° practically
/// the same (0.999 against 1.000) - it cannot tell them apart, and the reason is printed on the
/// cards. Both decks are double-headed: the König carries his name at the top and upside down at the
/// bottom, and the pips of a low card are laid out symmetrically. Turned by half a turn a Jass card
/// is the same picture, which is also why a proposal that gets this wrong costs so little: the class
/// is right either way, and only B₂'s own crops are then stored a little less consistently
/// (see "Real photos in the dataset" in context/architecture/data-pipeline.md).
/// </summary>
internal static class ObbCorners
{
    /// <summary>The corners as a label writes them, the card standing on its feet on the photo.</summary>
    public static Vector2[] Upright(IReadOnlyList<Vector2> quad)
    {
        if (quad.Count != 4) throw new ArgumentException("an oriented box has four corners", nameof(quad));

        // Wind it the way a label's corners run: TL→TR→BR→BL encloses a positive area while y points
        // down the screen, and the opposite winding would rectify the card mirrored.
        var corners = quad.ToArray();
        if (Area(corners) < 0) Array.Reverse(corners);

        // A Jass card is taller than it is wide, so its short side is the top edge: start where the
        // first edge is the shorter one and the crop comes out portrait rather than on its side.
        var candidate = Rotate(corners, Side(corners, 0) <= Side(corners, 1) ? 0 : 1);
        var turned = Turn(candidate);
        return Up(candidate).Y <= Up(turned).Y ? candidate : turned;
    }

    // The same card the other way up: the second of the two answers an oriented box allows.
    private static Vector2[] Turn(IReadOnlyList<Vector2> corners) =>
        [corners[2], corners[3], corners[0], corners[1]];

    // Twice the signed area (shoelace). Positive means the corners run the way TL→TR→BR→BL does on
    // screen, where y grows downwards.
    private static float Area(IReadOnlyList<Vector2> c)
    {
        float sum = 0;
        for (int i = 0; i < c.Count; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % c.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum;
    }

    private static float Side(IReadOnlyList<Vector2> c, int i) => Vector2.Distance(c[i], c[(i + 1) % 4]);

    private static Vector2[] Rotate(IReadOnlyList<Vector2> c, int start) =>
        [c[start], c[(start + 1) % 4], c[(start + 2) % 4], c[(start + 3) % 4]];

    // From the bottom edge to the top edge of the card: the more its y points up the screen (the more
    // negative), the more the card stands on its feet rather than on its head.
    private static Vector2 Up(IReadOnlyList<Vector2> c) => (c[0] + c[1]) / 2 - (c[2] + c[3]) / 2;
}
