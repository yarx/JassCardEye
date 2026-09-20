using System.Numerics;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Result of a render run: the finished image, the four projected image corners of the topmost
/// card (every label variant is derived from them) and the underlying scene. The caller is
/// responsible for disposal (<see cref="IDisposable"/>).
/// </summary>
public sealed class RenderResult(SKImage image, Vector2[] topCardCorners, Scene scene) : IDisposable
{
    /// <summary>The rendered square image.</summary>
    public SKImage Image { get; } = image;

    /// <summary>
    /// The four projected image corners of the topmost card (top-left, top-right, bottom-right,
    /// bottom-left); empty for a scene without cards.
    /// </summary>
    public Vector2[] TopCardCorners { get; } = topCardCorners;

    /// <summary>The rendered scene (for debugging/reproduction).</summary>
    public Scene Scene { get; } = scene;

    public void Dispose() => Image.Dispose();
}
