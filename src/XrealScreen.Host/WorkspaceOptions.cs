using XrealScreen.Core.Tracking;
using XrealScreen.Core.Workspace;

namespace XrealScreen.Host;

public enum TrackingSource
{
    /// <summary>XREAL One-series glasses over the USB network link.</summary>
    Glasses,

    /// <summary>Synthetic head sweep (no glasses needed).</summary>
    Simulated,

    /// <summary>No tracking: screens stay in front of the eyes (diagnostic).</summary>
    Fixed,
}

public enum WorkspaceKind
{
    /// <summary>The app tracks the head and renders curved screens (more screens; some latency).</summary>
    AppTracking,

    /// <summary>
    /// The glasses world-lock their own ultrawide image (OSD Anchor + UltraWide); the app splits it into
    /// 1–3 pixel-exact virtual screens. Most stable (ADR-0010).
    /// </summary>
    GlassesAnchor,
}

/// <summary>Everything a workspace session needs. Defaults = what was verified in the glasses on 2026-09-26.</summary>
public sealed record WorkspaceOptions
{
    public WorkspaceKind Kind { get; init; } = WorkspaceKind.AppTracking;

    public int ScreenCount { get; init; } = 3;

    /// <summary>Virtual screen (1-based) that becomes the Windows primary monitor during the session; 0 = keep.</summary>
    public int PrimaryScreen { get; init; }

    /// <summary>Move app windows off the glasses monitor (the workspace covers it) onto the workspace at start.</summary>
    public bool MoveWindowsFromGlasses { get; init; } = true;

    /// <summary>Glasses anchor: pixels between the screens. The seam is drawn, not see-through.</summary>
    public int AnchorGapPixels { get; init; } = 16;

    /// <summary>Glasses anchor: screen shape. Fill uses the whole glasses image.</summary>
    public AnchorAspect AnchorAspect { get; init; } = AnchorAspect.Fill;

    public UltrawideMode Mode { get; init; } = UltrawideMode.Off;

    public LayoutPreset Preset { get; init; } = LayoutPreset.Arc;

    public float DistanceMeters { get; init; } = 1.5f;

    public TrackingSource Source { get; init; } = TrackingSource.Glasses;

    /// <summary>off | balanced | strong | ultra (see <see cref="StabilizerSettings"/>).</summary>
    public string Stabilizer { get; init; } = "strong";

    public AutoCenterSettings AutoCenter { get; init; } = new();

    public int GlassesRefreshHz { get; init; } = 120;

    /// <summary>Pose prediction (ms). 20 ms lagged least in the glasses A/B test (12 / 20 ms, [verified-hw] 2026-09-27).</summary>
    public double PredictMs { get; init; } = 20;

    /// <summary>Late latch lead before the next glasses vblank (ms); 0 = latch right after vblank (ADR-0009).</summary>
    public double LatchLeadMs { get; init; }

    public float FilterBeta { get; init; } = 0.02f;

    public float AccelGate { get; init; } = 0.6f;

    /// <summary>Skip tilt correction while the head turns faster than this (rad/s); see <see cref="MadgwickFilter.RateGate"/>.</summary>
    public float RateGate { get; init; } = 0.09f;

    /// <summary>Which head rotations move the screens (default: turning, level with the horizon, no up/down).</summary>
    public TrackingAxes Axes { get; init; } = TrackingAxes.YawRoll;

    /// <summary>Neck model: leaning/nodding moves the eyes, so screens come closer when you lean in.</summary>
    public bool NeckModel { get; init; } = true;

    /// <summary>Run other monitors at the glasses refresh during the session (ADR-0009).</summary>
    public bool SyncDesktopRefresh { get; init; } = true;
}
