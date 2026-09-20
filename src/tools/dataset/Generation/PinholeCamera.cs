using System.Numerics;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Pinhole camera model: projects 3D world points onto pixel coordinates of a square image. Basis
/// for the perspective warping of the cards.
/// </summary>
public sealed class PinholeCamera
{
    private readonly Matrix4x4 _viewProjection;
    private readonly float _imageSize;

    public PinholeCamera(CameraPose pose, int imageSize)
    {
        _imageSize = imageSize;

        var forward = Vector3.Normalize(pose.Target - pose.Position);

        // "Up" is world Z where possible; for a near-vertical view (parallel to Z) fall back to
        // world Y to avoid a degenerate LookAt basis.
        var worldUp = MathF.Abs(Vector3.Dot(forward, Vector3.UnitZ)) > 0.95f
            ? Vector3.UnitY
            : Vector3.UnitZ;

        var view = Matrix4x4.CreateLookAt(pose.Position, pose.Target, worldUp);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            pose.FovYDegrees * MathF.PI / 180f,
            aspectRatio: 1f,
            nearPlaneDistance: 0.05f,
            farPlaneDistance: 100f);

        _viewProjection = view * projection;
    }

    /// <summary>Projects a world point onto pixel coordinates (origin top-left).</summary>
    public Vector2 Project(Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), _viewProjection);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;

        float px = (ndcX * 0.5f + 0.5f) * _imageSize;
        float py = (1f - (ndcY * 0.5f + 0.5f)) * _imageSize; // image Y points downward
        return new Vector2(px, py);
    }
}
