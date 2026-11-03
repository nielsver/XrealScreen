using System.Diagnostics;
using XrealScreen.Core.Hud;

namespace XrealScreen.App;

/// <summary>
/// Asks the installed Grok CLI what is on one monitor frame.
/// The CLI receives the picture. The app does not log it.
/// </summary>
internal static class GrokScreen
{
    public static async Task<string> SuggestAsync(byte[] bgra, int width, int height, CancellationToken cancellationToken)
    {
        string? exe = Executable();
        if (exe is null)
        {
            return "GROK CLI MISSING";
        }

        string directory = Path.Combine(Path.GetTempPath(), "XrealScreen", "hud");
        Directory.CreateDirectory(directory);
        string image = Path.Combine(directory, "screen.png");
        FramePng.Save(bgra, width, height, image);
        try
        {
            return await AskAsync(exe, directory, image, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<string> AskAsync(string exe, string directory, string image, CancellationToken cancellationToken)
    {
        string prompt =
            "Use the read_file tool on this PNG and on no other file: " + image + "\n"
            + "The image is the whole PC monitor. Reply in the same language as the writing on it. "
            + "At most 3 short lines. Say what is on the screen and one useful next step. "
            + "Do not invent anything that is not visible. No title. No bullet list.";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(70));
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["GROK_WORKFLOWS"] = "0";
        Argument(start, "--no-auto-update");
        Argument(start, "--disable-web-search");
        Argument(start, "--no-subagents");
        Argument(start, "--output-format");
        Argument(start, "json");
        Argument(start, "--max-turns");
        Argument(start, "4");
        Argument(start, "--reasoning-effort");
        Argument(start, "low");
        Argument(start, "--yolo");
        Argument(start, "--cwd");
        Argument(start, directory);
        Argument(start, "--tools");
        Argument(start, "read_file");
        Argument(start, "--allow");
        Argument(start, "Read");
        Argument(start, "--disallowed-tools");
        Argument(start, "run_terminal_cmd,write,search_replace,web_search,web_fetch,task,grep,list_dir,Agent,CallMcpTool");
        Argument(start, "-p");
        Argument(start, prompt);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return "NO SUGGESTION";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "NO SUGGESTION";
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        string stdout = string.Empty;
        string stderr = string.Empty;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            try
            {
                stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
                stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (GrokCliAnswer.Text(stdout) is { } late)
            {
                return GlancePrompt.ForHud(late);
            }

            return "TOO SLOW";
        }

        if (GrokCliAnswer.Text(stdout) is { } text)
        {
            return GlancePrompt.ForHud(text);
        }

        if (NeedsLogin(stderr) || NeedsLogin(stdout))
        {
            return "Sign in with grok login";
        }

        return "NO SUGGESTION";
    }

    private static string? Executable()
    {
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
        if (File.Exists(home))
        {
            return home;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim(), "grok.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool NeedsLogin(string text) =>
        text.Contains("grok login", StringComparison.OrdinalIgnoreCase)
        || text.Contains("not logged", StringComparison.OrdinalIgnoreCase)
        || text.Contains("not signed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("authentication", StringComparison.OrdinalIgnoreCase);

    private static void Argument(ProcessStartInfo start, string value) => start.ArgumentList.Add(value);

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
