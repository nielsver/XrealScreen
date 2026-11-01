namespace XrealScreen.Vision.Hands;

/// <summary>
/// Maps a hand point in camera pixels to a position on the target screen area (u, v in 0..1, v down) with an
/// affine transform. The Eye is fixed to the displays, so one calibration per user holds until the user changes
/// how they sit (ADR-0016). Fit from points where the user pinched at known targets.
/// </summary>
/// <param name="A">u = A·x + B·y + C</param>
/// <param name="D">v = D·x + E·y + F</param>
public sealed record HandCalibration(double A, double B, double C, double D, double E, double F)
{
    /// <summary>
    /// Without calibration: the middle of the camera image (x 25–75 %, y 20–70 %, where a raised hand usually is)
    /// spans the whole screen area.
    /// </summary>
    public static HandCalibration Default(int cameraWidth, int cameraHeight)
    {
        double x0 = 0.25 * cameraWidth, x1 = 0.75 * cameraWidth;
        double y0 = 0.20 * cameraHeight, y1 = 0.70 * cameraHeight;
        return new HandCalibration(1 / (x1 - x0), 0, -x0 / (x1 - x0), 0, 1 / (y1 - y0), -y0 / (y1 - y0));
    }

    public (double U, double V) Map(double x, double y) => (A * x + B * y + C, D * x + E * y + F);

    /// <summary>Least-squares affine fit; needs at least 3 points that are not on one line.</summary>
    public static HandCalibration Fit(IReadOnlyList<CalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            throw new ArgumentException("At least 3 calibration points are needed.", nameof(points));
        }

        // normal equations M·p = r for p = (a, b, c) with rows (x, y, 1)
        double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = points.Count;
        double sxu = 0, syu = 0, su = 0, sxv = 0, syv = 0, sv = 0;
        foreach (var p in points)
        {
            sxx += p.CameraX * p.CameraX;
            sxy += p.CameraX * p.CameraY;
            sx += p.CameraX;
            syy += p.CameraY * p.CameraY;
            sy += p.CameraY;
            sxu += p.CameraX * p.U;
            syu += p.CameraY * p.U;
            su += p.U;
            sxv += p.CameraX * p.V;
            syv += p.CameraY * p.V;
            sv += p.V;
        }

        double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        var (a, b, c) = Solve3(m, sxu, syu, su);
        var (d, e, f) = Solve3(m, sxv, syv, sv);
        return new HandCalibration(a, b, c, d, e, f);
    }

    /// <summary>Root-mean-square error of the fit in screen units (0..1); above ~0.05 the user should calibrate again.</summary>
    public double RmsError(IReadOnlyList<CalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        double sum = 0;
        foreach (var p in points)
        {
            var (u, v) = Map(p.CameraX, p.CameraY);
            sum += (u - p.U) * (u - p.U) + (v - p.V) * (v - p.V);
        }

        return Math.Sqrt(sum / points.Count);
    }

    private static (double, double, double) Solve3(double[,] m, double r0, double r1, double r2)
    {
        double det = Det(m[0, 0], m[0, 1], m[0, 2], m[1, 0], m[1, 1], m[1, 2], m[2, 0], m[2, 1], m[2, 2]);
        if (Math.Abs(det) < 1e-9)
        {
            throw new ArgumentException("Calibration points lie on one line; spread the targets out.");
        }

        return (Det(r0, m[0, 1], m[0, 2], r1, m[1, 1], m[1, 2], r2, m[2, 1], m[2, 2]) / det,
                Det(m[0, 0], r0, m[0, 2], m[1, 0], r1, m[1, 2], m[2, 0], r2, m[2, 2]) / det,
                Det(m[0, 0], m[0, 1], r0, m[1, 0], m[1, 1], r1, m[2, 0], m[2, 1], r2) / det);
    }

    private static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
        a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
}

/// <summary>A pinch at a known target: where the hand point was in the camera, and where the target was (0..1).</summary>
public readonly record struct CalibrationPoint(double CameraX, double CameraY, double U, double V);
