using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// Builds the 3x3 matrices that convert linear RGB between sets of primaries. Every matrix is
/// row-major in a nine-element array.
/// </summary>
public static class Colorimetry
{
    /// <summary>
    /// Builds the matrix that converts linear RGB in one set of primaries into another.
    /// </summary>
    /// <param name="source">The primaries of the input.</param>
    /// <param name="dest">The primaries of the output.</param>
    /// <param name="adaptWhite">Whether a Bradford adaptation maps the source white onto the destination white.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] GamutMatrix(in Chromaticities source, in Chromaticities dest, bool adaptWhite = true)
    {
        var toXyz = RgbToXyz(source);
        var fromXyz = Invert(RgbToXyz(dest));
        if (!adaptWhite)
        {
            return Multiply(fromXyz, toXyz);
        }

        var adapt = BradfordAdaptation(
            WhiteXyz(source.Wx, source.Wy), WhiteXyz(dest.Wx, dest.Wy));
        return Multiply(fromXyz, Multiply(adapt, toXyz));
    }

    /// <summary>
    /// Builds the matrix from linear RGB to XYZ, adapted to the D50 white of ICC profiles.
    /// </summary>
    /// <param name="c">The primaries of the input.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] RgbToXyzD50(in Chromaticities c)
    {
        var adapt = BradfordAdaptation(WhiteXyz(c.Wx, c.Wy), [0.9642, 1.0, 0.8249]);
        return Multiply(adapt, RgbToXyz(c));
    }

    /// <summary>
    /// Builds the matrix from linear RGB to XYZ at the primaries' own white point.
    /// </summary>
    /// <param name="c">The primaries of the input.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] RgbToXyz(in Chromaticities c)
    {
        double[] primaries =
        [
            c.Rx / c.Ry, c.Gx / c.Gy, c.Bx / c.By,
            1, 1, 1,
            (1 - c.Rx - c.Ry) / c.Ry, (1 - c.Gx - c.Gy) / c.Gy, (1 - c.Bx - c.By) / c.By,
        ];
        var white = WhiteXyz(c.Wx, c.Wy);
        var scale = Solve(primaries, white);
        return
        [
            primaries[0] * scale[0], primaries[1] * scale[1], primaries[2] * scale[2],
            primaries[3] * scale[0], primaries[4] * scale[1], primaries[5] * scale[2],
            primaries[6] * scale[0], primaries[7] * scale[1], primaries[8] * scale[2],
        ];
    }

    private static double[] WhiteXyz(double x, double y) => [x / y, 1, (1 - x - y) / y];

    private static double[] BradfordAdaptation(double[] sourceWhite, double[] destWhite)
    {
        double[] bradford =
        [
            0.8951, 0.2664, -0.1614,
            -0.7502, 1.7135, 0.0367,
            0.0389, -0.0685, 1.0296,
        ];
        var inverse = Invert(bradford);
        var sourceCone = Apply(bradford, sourceWhite);
        var destCone = Apply(bradford, destWhite);
        double[] scale =
        [
            destCone[0] / sourceCone[0], 0, 0,
            0, destCone[1] / sourceCone[1], 0,
            0, 0, destCone[2] / sourceCone[2],
        ];
        return Multiply(inverse, Multiply(scale, bradford));
    }

    /// <summary>
    /// Tells whether a gamut matrix can make a negative channel from a valid input, which means the
    /// source gamut is larger than the destination.
    /// </summary>
    /// <param name="matrix">A matrix from <see cref="GamutMatrix"/>.</param>
    /// <returns><see langword="true"/> when any element is below -1e-6.</returns>
    public static bool LeavesGamut(double[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        foreach (var value in matrix)
        {
            if (value < -1e-6)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Multiplies two 3x3 matrices.
    /// </summary>
    /// <param name="a">The left matrix.</param>
    /// <param name="b">The right matrix, which applies first.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] Multiply(double[] a, double[] b)
    {
        var result = new double[9];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                result[row * 3 + col] =
                    a[row * 3] * b[col] + a[row * 3 + 1] * b[3 + col] + a[row * 3 + 2] * b[6 + col];
            }
        }

        return result;
    }

    /// <summary>
    /// Multiplies a 3x3 matrix by a column vector.
    /// </summary>
    /// <param name="m">The matrix.</param>
    /// <param name="v">The three-element vector.</param>
    /// <returns>A new three-element vector.</returns>
    public static double[] Apply(double[] m, double[] v) =>
        [
            m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
            m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
            m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
        ];

    /// <summary>
    /// Inverts a 3x3 matrix. It throws <see cref="InvalidOperationException"/> when the matrix is
    /// singular.
    /// </summary>
    /// <param name="m">The matrix to invert.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] Invert(double[] m)
    {
        var det =
            m[0] * (m[4] * m[8] - m[5] * m[7]) -
            m[1] * (m[3] * m[8] - m[5] * m[6]) +
            m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-12)
        {
            throw new InvalidOperationException("chromaticity matrix is singular");
        }

        var inv = 1.0 / det;
        return
        [
            (m[4] * m[8] - m[5] * m[7]) * inv,
            (m[2] * m[7] - m[1] * m[8]) * inv,
            (m[1] * m[5] - m[2] * m[4]) * inv,
            (m[5] * m[6] - m[3] * m[8]) * inv,
            (m[0] * m[8] - m[2] * m[6]) * inv,
            (m[2] * m[3] - m[0] * m[5]) * inv,
            (m[3] * m[7] - m[4] * m[6]) * inv,
            (m[1] * m[6] - m[0] * m[7]) * inv,
            (m[0] * m[4] - m[1] * m[3]) * inv,
        ];
    }

    private static double[] Solve(double[] m, double[] v) => Apply(Invert(m), v);
}
