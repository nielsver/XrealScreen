using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using XrealScreen.Core.Hud;

namespace XrealScreen.App;

/// <summary>PC actions the glance pins can run, and the short text the HUD is able to draw.</summary>
internal static class GlanceInput
{
    public static void Tap(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, 0);
        keybd_event(virtualKey, 0, 2, 0);
    }

    public const byte MediaPlayPause = 0xB3;

    public const byte VolumeMute = 0xAD;

    /// <summary>
    /// Reads the clipboard and calls <paramref name="done"/> once with text the HUD can draw.
    /// The callback can run on another thread. Clipboard failures become "EMPTY".
    /// </summary>
    public static void ReadClipboard(Action<string> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                done("EMPTY");
                return;
            }

            var operation = content.GetTextAsync();
            if (operation.Status == Windows.Foundation.AsyncStatus.Completed)
            {
                done(Fit(operation.GetResults()));
                return;
            }

            if (operation.Status is Windows.Foundation.AsyncStatus.Error or Windows.Foundation.AsyncStatus.Canceled)
            {
                done("EMPTY");
                return;
            }

            operation.Completed = (op, status) =>
            {
                try
                {
                    done(status == Windows.Foundation.AsyncStatus.Completed ? Fit(op.GetResults()) : "EMPTY");
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
                {
                    done("EMPTY");
                }
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            done("EMPTY");
        }
    }

    public static string Fit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "EMPTY";
        }

        var drawn = new StringBuilder(18);
        foreach (char character in text.Trim().ToUpperInvariant())
        {
            if (character is '\r' or '\n')
            {
                break;
            }

            if (character != ' ' && !HudFont.Knows(character))
            {
                continue;
            }

            drawn.Append(character);
            if (drawn.Length == 18)
            {
                break;
            }
        }

        return drawn.Length == 0 ? "EMPTY" : drawn.ToString().Trim();
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
}
