using System.Numerics;
using SkiaSharp;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Computes the perspective map (3×3 homography) between four point pairs and returns it as an
/// <see cref="SKMatrix"/>, with which a card image is warped exactly into an arbitrary quad on the
/// canvas.
/// </summary>
public static class Homography
{
    /// <summary>
    /// Determines the matrix that maps <paramref name="source"/> (image-pixel corners) to
    /// <paramref name="destination"/> (canvas corners). Both arrays must contain four corresponding
    /// points in the same order.
    /// </summary>
    public static SKMatrix Compute(Vector2[] source, Vector2[] destination)
    {
        // 8 unknowns (h11..h32), h33 = 1. Build A·h = b from 4 point pairs.
        var a = new double[8, 8];
        var b = new double[8];

        for (int k = 0; k < 4; k++)
        {
            double x = source[k].X, y = source[k].Y;
            double u = destination[k].X, v = destination[k].Y;

            int r0 = 2 * k;
            a[r0, 0] = x; a[r0, 1] = y; a[r0, 2] = 1;
            a[r0, 6] = -x * u; a[r0, 7] = -y * u;
            b[r0] = u;

            int r1 = 2 * k + 1;
            a[r1, 3] = x; a[r1, 4] = y; a[r1, 5] = 1;
            a[r1, 6] = -x * v; a[r1, 7] = -y * v;
            b[r1] = v;
        }

        double[] h = Solve(a, b);

        return new SKMatrix
        {
            ScaleX = (float)h[0], SkewX = (float)h[1], TransX = (float)h[2],
            SkewY  = (float)h[3], ScaleY = (float)h[4], TransY = (float)h[5],
            Persp0 = (float)h[6], Persp1 = (float)h[7], Persp2 = 1f,
        };
    }

    /// <summary>Gaussian elimination with partial (column) pivoting for an 8×8 system.</summary>
    private static double[] Solve(double[,] a, double[] b)
    {
        const int n = 8;
        for (int col = 0; col < n; col++)
        {
            // Find the pivot row.
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    pivot = row;

            if (pivot != col)
            {
                for (int c = 0; c < n; c++)
                    (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            double diag = a[col, col];
            for (int row = 0; row < n; row++)
            {
                if (row == col) continue;
                double factor = a[row, col] / diag;
                if (factor == 0) continue;
                for (int c = col; c < n; c++)
                    a[row, c] -= factor * a[col, c];
                b[row] -= factor * b[col];
            }
        }

        var x = new double[n];
        for (int i = 0; i < n; i++)
            x[i] = b[i] / a[i, i];
        return x;
    }
}
