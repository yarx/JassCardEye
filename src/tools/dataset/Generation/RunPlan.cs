using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// What an image's index decides, as opposed to what its random stream draws.
/// </summary>
/// <param name="Deck">The deck the whole pile is dealt from.</param>
/// <param name="TiltDegrees">The camera tilt away from vertical.</param>
public readonly record struct ScenePlan(Deck Deck, float TiltDegrees);

/// <summary>
/// The plan of a run: which deck each image shows and from how flat an angle, derived from the
/// image index rather than drawn.
///
/// Two properties have to hold exactly rather than on average, which is why they are computed and
/// not sampled:
///
/// <b>The tilt sweeps the whole range evenly.</b> Flat viewing angles are the hardest case for the
/// model, so they must be exactly as common as steep ones. Drawing the tilt uniformly from the
/// whole range would achieve that only on average; deriving the angle from the index makes it exact.
///
/// <b>Each deck gets exactly half the run.</b> Jass is played with one deck or the other, so a pile
/// is never mixed and the model has to learn two full decks. Splitting the run by a coin flip would
/// leave the halves unequal, and - worse - a deck chosen independently of the sweep could end up
/// with more flat frames than the other, which is precisely the imbalance the sweep exists to
/// remove. Both therefore come from the same permuted position: even positions are French, odd ones
/// German. Each deck then holds half the images *and* an even sweep of its own, every second step of
/// the full one.
///
/// This is deliberately not a <see cref="RandomizerOptions"/> value. Nothing here is drawn, and the
/// upper bound is not a preference but a property of the renderer (see <see cref="MaxTiltDegrees"/>).
/// </summary>
public static class RunPlan
{
    /// <summary>Straight down onto the table.</summary>
    public const float MinTiltDegrees = 0f;

    /// <summary>
    /// The flattest view the renderer is sound at - not a round number.
    ///
    /// The horizon enters the frame at about 90° minus half the field of view, and the field of view
    /// is drawn from 45–55°. Measured over 20 000 scenes per angle, no frame shows the horizon at
    /// 62° and 14.8 % do at 63°. Once it is in view, <c>PlaneProjection.ForCamera</c> back-projects
    /// image corners that lie behind the camera, the world rectangle blows up from about 13 to 377
    /// units, and the table texture and the plane light are drawn nowhere - the frame comes out
    /// white above the horizon. Raising this means fixing that first.
    /// </summary>
    public const float MaxTiltDegrees = 60f;

    /// <summary>The deck and tilt of image <paramref name="index"/> in a run of <paramref name="count"/>.</summary>
    public static ScenePlan For(int index, int count)
    {
        long position = Position(index, count);

        // The half-step keeps the sweep symmetric: it starts half a step above the minimum and ends
        // half a step below the maximum, instead of touching one end and missing the other.
        float tilt = MinTiltDegrees
                     + (MaxTiltDegrees - MinTiltDegrees) * (position + 0.5f) / count;

        return new ScenePlan(position % 2 == 0 ? Deck.French : Deck.German, tilt);
    }

    /// <summary>
    /// Where image <paramref name="index"/> sits in the sweep. A permutation of 0..count-1, so every
    /// place is taken exactly once.
    ///
    /// The permutation is what keeps a run from being sorted by angle. The set of angles is the same
    /// either way, but this way the first few hundred images - what anyone looks at when checking a
    /// run by eye - already span the whole range instead of all being near-vertical.
    /// </summary>
    private static long Position(int index, int count) => (long)index * Step(count) % count;

    /// <summary>
    /// The step of the index permutation: roughly <c>count / φ</c>, raised until it shares no factor
    /// with <paramref name="count"/> so that <c>i * step mod count</c> visits every index.
    ///
    /// The golden ratio is the classic choice for spreading a sequence: successive multiples of
    /// count/φ land about as far from each other as they can. A single large prime constant would
    /// also be coprime to any smaller count, but a constant can be congruent to 1 for a given
    /// count and then the permutation is the identity: at count 12
    /// the well-known 2654435761 does exactly that, and the run comes out sorted by angle. Deriving
    /// the step from the count cannot fall into that.
    /// </summary>
    private static long Step(int count)
    {
        long step = Math.Max(1, (long)(count / 1.6180339887498949)) | 1L;
        while (Gcd(step, count) != 1)
            step += 2;
        return step;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
