using System.Net.Sockets;

namespace XrealScreen.Device.XrealOne;

/// <summary>A short read of the IMU stream, used to tell a live link from a port that only accepts.</summary>
public static class OneStreamSample
{
    public static async Task<ImuSampleResult> ReadAsync(string host, int port, TimeSpan window, CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connect.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ImuSampleResult(0, 0, "not reachable");
        }

        using var stream = client.GetStream();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        read.CancelAfter(window + TimeSpan.FromMilliseconds(400));
        var framer = new OneReportFramer();
        int imu = 0;
        int magnetometer = 0;
        var buffer = new byte[8192];
        long end = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(window.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        try
        {
            while (System.Diagnostics.Stopwatch.GetTimestamp() < end)
            {
                int count = await stream.ReadAsync(buffer, read.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                framer.Append(buffer.AsSpan(0, count), report =>
                {
                    if (report.Type == OneReportType.Imu)
                    {
                        imu++;
                    }
                    else
                    {
                        magnetometer++;
                    }
                });
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            return new ImuSampleResult(imu, magnetometer, ex.Message);
        }

        return new ImuSampleResult(imu, magnetometer, null);
    }
}

public readonly record struct ImuSampleResult(int ImuReports, int MagnetometerReports, string? Error);
