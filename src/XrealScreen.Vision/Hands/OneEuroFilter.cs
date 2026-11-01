namespace XrealScreen.Vision.Hands;

/// <summary>
/// One Euro filter (Casiez, Roussel, Vogel, CHI 2012): smooths strongly when the value is still and little when it
/// moves fast, so a pointer is steady without lagging. Own implementation of the published algorithm.
/// </summary>
public sealed class OneEuroFilter(float minCutoffHz = 1.0f, float beta = 0.05f, float derivativeCutoffHz = 1.0f)
{
    private bool _initialized;
    private float _value;
    private float _derivative;
    private double _lastSeconds;

    public float MinCutoffHz { get; set; } = minCutoffHz;

    public float Beta { get; set; } = beta;

    public float Filter(float value, double timeSeconds)
    {
        if (!_initialized || timeSeconds <= _lastSeconds)
        {
            _initialized = true;
            _value = value;
            _derivative = 0;
            _lastSeconds = timeSeconds;
            return value;
        }

        float dt = (float)(timeSeconds - _lastSeconds);
        _lastSeconds = timeSeconds;
        float rawDerivative = (value - _value) / dt;
        _derivative += Alpha(derivativeCutoffHz, dt) * (rawDerivative - _derivative);
        float cutoff = MinCutoffHz + Beta * MathF.Abs(_derivative);
        _value += Alpha(cutoff, dt) * (value - _value);
        return _value;
    }

    public void Reset() => _initialized = false;

    private static float Alpha(float cutoffHz, float dt)
    {
        float tau = 1f / (2 * MathF.PI * cutoffHz);
        return 1f / (1f + tau / dt);
    }
}
