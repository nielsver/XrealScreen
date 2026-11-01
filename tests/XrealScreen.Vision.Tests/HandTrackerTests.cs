using XrealScreen.Vision.Hands;

namespace XrealScreen.Vision.Tests;

/// <summary>Needs the converted models (tools/Convert-HandModels.ps1); skipped when they are missing.</summary>
public class HandTrackerTests
{
    [Fact]
    public void EmptyFrame_FindsNoHand_AndRunsThePalmDetector()
    {
        string? dir = ModelDirectory();
        if (dir is null)
        {
            Assert.Skip("Hand models not found (run tools/Convert-HandModels.ps1 or set XREALSCREEN_HAND_MODELS).");
        }

        using var tracker = new HandTracker(new HandTrackerOptions { ModelDirectory = dir });
        var frame = new byte[512 * 378];
        Array.Fill(frame, (byte)90);

        var result = tracker.Process(frame, 504, 378, 512, 0);

        Assert.Null(result);
        Assert.Equal(1, tracker.PalmRuns);
    }

    [Fact]
    public void MissingModels_ThrowWithAHint()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => new HandTracker(new HandTrackerOptions { ModelDirectory = Path.GetTempPath() + Guid.NewGuid() }));

        Assert.Contains("Convert-HandModels", ex.Message, StringComparison.Ordinal);
    }

    internal static string? ModelDirectory()
    {
        if (Environment.GetEnvironmentVariable("XREALSCREEN_HAND_MODELS") is { Length: > 0 } env && HandTracker.ModelsExist(env))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "artifacts", "models", "hands");
            if (HandTracker.ModelsExist(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
