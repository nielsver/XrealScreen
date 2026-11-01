namespace XrealScreen.Vision.Hands;

/// <summary>
/// A square region of the camera image, in pixels, rotated by <see cref="Rotation"/> radians
/// (the hand region the landmark model looks at; rotated so the fingers point up in the crop).
/// </summary>
public readonly record struct RotatedRect(float CenterX, float CenterY, float Size, float Rotation)
{
    /// <summary>Maps a point of the unit crop (u, v in 0..1, v down) to image pixels.</summary>
    public (float X, float Y) ToImage(float u, float v)
    {
        float nx = (u - 0.5f) * Size;
        float ny = (v - 0.5f) * Size;
        float c = MathF.Cos(Rotation);
        float s = MathF.Sin(Rotation);
        return (CenterX + nx * c - ny * s, CenterY + nx * s + ny * c);
    }
}
