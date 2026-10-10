using System.Runtime.CompilerServices;
using XrealScreen.Core.Abstractions;
using XrealScreen.Core.Tracking;
using XrealScreen.Device.XrealOne;

namespace XrealScreen.Sdk;

/// <summary>Connection settings for <see cref="XrealGlasses"/>.</summary>
public sealed record XrealGlassesOptions
{
    /// <summary>IP address of the glasses on their USB network adapter. Default <c>169.254.2.1</c>.</summary>
    public string Host { get; init; } = OneEndpoints.Default.Host;

    /// <summary>How long one connection attempt may take. Default 3 seconds.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Wait between connection attempts. Default 2 seconds.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Raised when the glasses connect or the connection is lost.</summary>
public sealed class GlassesConnectionChangedEventArgs(bool isConnected, string? error) : EventArgs
{
    /// <summary>True when poses are arriving.</summary>
    public bool IsConnected { get; } = isConnected;

    /// <summary>Why the last connection attempt failed or ended, when known.</summary>
    public string? Error { get; } = error;
}

/// <summary>
/// Head tracking from XREAL One / One Pro / 1S glasses: reads the IMU over the glasses' USB
/// network adapter and fuses it into head poses (gyro bias estimation, Madgwick filter, recenter).
/// </summary>
/// <remarks>
/// <para>Connection problems never throw: right after the glasses are plugged in their USB network
/// is not up yet, and a cable can be pulled at any time, so <see cref="ReadPosesAsync"/> keeps
/// retrying every <see cref="XrealGlassesOptions.RetryDelay"/> and reports changes through
/// <see cref="ConnectionChanged"/>.</para>
/// <para>The glasses send to one reader at a time: do not run the XrealScreen app's workspace or
/// head look at the same time (use the app's local API instead, see <c>XrealScreen.Sdk.Client</c>).</para>
/// </remarks>
/// <example>
/// <code>
/// var glasses = new XrealGlasses();
/// await foreach (var pose in glasses.ReadPosesAsync(cancellationToken))
/// {
///     Console.WriteLine($"yaw {pose.Yaw:F1}  pitch {pose.Pitch:F1}  roll {pose.Roll:F1}");
/// }
/// </code>
/// </example>
public sealed class XrealGlasses
{
    private readonly IImuSource _source;
    private readonly TimeSpan _retryDelay;
    private readonly HeadTracker _tracker = new();
    private readonly Lock _poseLock = new();
    private GlassesPose? _latest;
    private int _reading;
    private volatile bool _connected;
    private bool _waitReported; // reading thread only: the first failed attempt of this outage was reported

    /// <summary>Glasses on the default address (<c>169.254.2.1</c>).</summary>
    public XrealGlasses()
        : this(new XrealGlassesOptions())
    {
    }

    /// <summary>Glasses with custom connection settings.</summary>
    public XrealGlasses(XrealGlassesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _source = new XrealOneImuSource(new XrealOneImuOptions
        {
            Endpoints = OneEndpoints.Default with { Host = options.Host },
            ConnectTimeout = options.ConnectTimeout,
        });
        _retryDelay = options.RetryDelay;
    }

    /// <summary>
    /// Any IMU source: a recording (<c>XrimuReplaySource</c>), the simulator
    /// (<c>SyntheticImuSource</c>) or your own device. Useful for tests and demos without glasses.
    /// </summary>
    public XrealGlasses(IImuSource source, TimeSpan? retryDelay = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Raised on the reading thread when poses start arriving or the connection is lost.</summary>
    public event EventHandler<GlassesConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>Where the poses come from, e.g. <c>XREAL One-series @ 169.254.2.1:52998</c>.</summary>
    public string SourceName => _source.Name;

    /// <summary>True while poses are arriving.</summary>
    public bool IsConnected => _connected;

    /// <summary>
    /// The newest pose, or null before the first one. Safe to read from any thread while another
    /// thread runs <see cref="ReadPosesAsync"/> (e.g. a render loop that only needs the latest pose).
    /// </summary>
    public GlassesPose? LatestPose
    {
        get
        {
            lock (_poseLock)
            {
                return _latest;
            }
        }
    }

    /// <summary>Number of IMU samples fused so far.</summary>
    public long SampleCount => Interlocked.Read(ref _sampleCount);

    private long _sampleCount;

    /// <summary>Makes the current head direction the center (yaw 0) on the next sample. Thread-safe.</summary>
    public void Recenter() => _tracker.RequestRecenter();

    /// <summary>
    /// Streams head poses until <paramref name="cancellationToken"/> is cancelled, reconnecting as
    /// needed. Only one enumeration may run at a time; other consumers read <see cref="LatestPose"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another enumeration is already running.</exception>
    public async IAsyncEnumerable<GlassesPose> ReadPosesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
        {
            throw new InvalidOperationException("Poses are already being read. Use LatestPose for a second consumer.");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? error = null;
                await using (var samples = _source.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken))
                {
                    while (true)
                    {
                        bool hasSample;
                        try
                        {
                            hasSample = await samples.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            hasSample = false;
                        }
                        catch (IOException ex)
                        {
                            error = ex.Message;
                            hasSample = false;
                        }

                        if (!hasSample)
                        {
                            break;
                        }

                        var pose = new GlassesPose(_tracker.Update(samples.Current));
                        lock (_poseLock)
                        {
                            _latest = pose;
                        }

                        Interlocked.Increment(ref _sampleCount);
                        ReportConnected();
                        yield return pose;
                    }
                }

                ReportDisconnected(error ?? "The IMU stream ended.");
                if (!await DelayAsync(_retryDelay, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            if (_connected)
            {
                _connected = false;
                ConnectionChanged?.Invoke(this, new GlassesConnectionChangedEventArgs(false, null));
            }

            _waitReported = false;
            Volatile.Write(ref _reading, 0);
        }
    }

    private void ReportConnected()
    {
        if (!_connected)
        {
            _connected = true;
            _waitReported = false;
            ConnectionChanged?.Invoke(this, new GlassesConnectionChangedEventArgs(true, null));
        }
    }

    /// <summary>Once per outage: when a connection is lost, or when the first attempt fails.</summary>
    private void ReportDisconnected(string error)
    {
        if (_connected || !_waitReported)
        {
            _connected = false;
            _waitReported = true;
            ConnectionChanged?.Invoke(this, new GlassesConnectionChangedEventArgs(false, error));
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
