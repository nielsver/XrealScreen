using XrealScreen.Device.XrealOne;

namespace XrealScreen.Device.Tests;

public class XrcamFileTests
{
    [Fact]
    public async Task RoundTripsFramesWithoutRowPadding()
    {
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(Path.GetTempPath(), $"xrcam-test-{Guid.NewGuid():N}.xrcam");
        try
        {
            var framer = new EyeFrameFramer();
            var frames = new List<EyeFrame>();
            framer.Append(EyeFrameFramerTests.BuildFrame(100), frames.Add);
            framer.Append(EyeFrameFramerTests.BuildFrame(200), frames.Add);

            var received = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
            await using (var writer = await XrcamWriter.CreateAsync(path, "test\nmeta", ct))
            {
                foreach (var f in frames)
                {
                    await writer.WriteAsync(f, received, ct);
                }

                Assert.Equal(2, writer.FramesWritten);
            }

            Assert.Equal("test meta", XrcamReader.ReadMetadata(path));
            var read = new List<XrcamReader.Frame>();
            await foreach (var f in XrcamReader.ReadAsync(path, ct))
            {
                read.Add(f);
            }

            Assert.Equal([100UL, 200UL], read.Select(f => f.TimestampNs));
            Assert.All(read, f => Assert.Equal((6, 4, received), (f.Width, f.Height, f.ReceivedUtc)));
            Assert.Equal(frames[0].ToPackedArray(), read[0].Pixels);
            frames.ForEach(f => f.Dispose());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsOtherFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(Path.GetTempPath(), $"xrcam-test-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllTextAsync(path, "XRCAP1\nnot a camera file\n", ct);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await foreach (var _ in XrcamReader.ReadAsync(path, ct))
                {
                }
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
