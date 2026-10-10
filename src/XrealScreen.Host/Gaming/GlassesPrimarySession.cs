using XrealScreen.Display;

namespace XrealScreen.Host.Gaming;

/// <summary>
/// Makes the glasses the Windows primary monitor while a game runs, so games open on them (ADR-0011).
/// The previous layout goes to the same snapshot file as a workspace session, so app start and
/// <c>xrs recover</c> restore it after a crash. Dispose restores it.
/// </summary>
public sealed class GlassesPrimarySession : IAsyncDisposable
{
    private readonly CcdDisplayTopology _topology;
    private readonly LayoutSnapshotStore _store;

    private GlassesPrimarySession(CcdDisplayTopology topology, LayoutSnapshotStore store)
    {
        _topology = topology;
        _store = store;
    }

    /// <summary>Returns null when nothing had to change (the glasses are already primary).</summary>
    /// <exception cref="InvalidOperationException">No glasses, or a workspace (or crashed session) owns the snapshot.</exception>
    public static async Task<GlassesPrimarySession?> StartAsync(CancellationToken cancellationToken)
    {
        var topology = new CcdDisplayTopology();
        var store = new LayoutSnapshotStore();
        var monitors = topology.GetActiveMonitors();
        var glasses = GlassesDisplayLocator.FindGlasses(monitors) ?? throw new InvalidOperationException("Glasses display not found.");
        if (glasses.IsPrimary)
        {
            return null;
        }

        if (store.Exists)
        {
            throw new InvalidOperationException("A workspace is running or did not stop cleanly; stop it or run `xrs recover` first.");
        }

        await store.SaveAsync(topology.CaptureLayout(), cancellationToken).ConfigureAwait(false);
        try
        {
            topology.ApplyLayout(GameDisplay.GlassesPrimary(monitors, glasses.GdiDeviceName));
        }
        catch
        {
            store.Delete();
            throw;
        }

        return new GlassesPrimarySession(topology, store);
    }

    public async ValueTask DisposeAsync()
    {
        var saved = await _store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (saved is not null)
        {
            _topology.ApplyLayout(saved);
            _store.Delete();
        }
    }
}
