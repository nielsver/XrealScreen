using System.Text.Json;

namespace XrealScreen.Core.Hud;

/// <summary>Reads the visible reply from <c>grok --output-format json</c>.</summary>
public static class GrokCliAnswer
{
    /// <summary>The assistant text, or null when the run failed or said nothing.</summary>
    public static string? Text(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        string trimmed = stdout.Trim().TrimStart('\uFEFF');
        if (TryRead(trimmed, out string? whole))
        {
            return BlankToNull(whole);
        }

        string? found = null;
        foreach (string raw in trimmed.Split('\n'))
        {
            if (TryRead(raw.Trim(), out string? line) && !string.IsNullOrWhiteSpace(line))
            {
                found = line;
            }
        }

        if (found is not null)
        {
            return found;
        }

        int start = trimmed.IndexOf('{');
        int end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start && TryRead(trimmed[start..(end + 1)], out string? slice))
        {
            return BlankToNull(slice);
        }

        return null;
    }

    private static string? BlankToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static bool TryRead(string json, out string? text)
    {
        text = null;
        if (json.Length == 0 || json[0] != '{')
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("is_error", out JsonElement failed) && failed.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (root.TryGetProperty("type", out JsonElement kind) && kind.GetString() == "error")
            {
                return true;
            }

            if (root.TryGetProperty("text", out JsonElement direct) && direct.ValueKind == JsonValueKind.String)
            {
                text = direct.GetString();
                return true;
            }

            if (root.TryGetProperty("type", out JsonElement type)
                && type.GetString() == "result"
                && root.TryGetProperty("result", out JsonElement result)
                && result.ValueKind == JsonValueKind.String)
            {
                text = result.GetString();
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
