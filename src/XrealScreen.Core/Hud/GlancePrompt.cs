using System.Text;
using System.Text.Json;

namespace XrealScreen.Core.Hud;

/// <summary>
/// The one-shot screen note. Builds the request and reads the answer. The app sends it.
/// Screen text is not stored on the model side (<c>store: false</c>).
/// </summary>
public static class GlancePrompt
{
    public const string Model = "grok-4.7";

    public const int MaxScreenChars = 6000;

    public const int LineChars = 46;

    public const int Lines = 3;

    private const string SystemPrompt =
        "The user is wearing glasses and glanced at their PC screen. You are given the text recognized on that screen. " +
        "Reply in the same language. At most 3 short lines, no title and no bullet list. " +
        "Say what is on screen and the one useful next step. Do not invent things that are not in the text.";

    public static string RequestJson(string? screenText)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["store"] = false,
            ["input"] = new object[]
            {
                new Dictionary<string, string> { ["role"] = "system", ["content"] = SystemPrompt },
                new Dictionary<string, string> { ["role"] = "user", ["content"] = Clip(screenText) },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>The model's visible answer, or null when the body has none.</summary>
    public static string? Answer(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("output_text", out JsonElement direct)
            && direct.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(direct.GetString()))
        {
            return direct.GetString();
        }

        if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out JsonElement type) || type.GetString() != "output_text")
                {
                    continue;
                }

                if (part.TryGetProperty("text", out JsonElement text) && text.GetString() is { Length: > 0 } value)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(value);
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>At most three short lines the card can show.</summary>
    public static string ForHud(string? text)
    {
        string flat = Flatten(text);
        if (flat.Length == 0)
        {
            return "NO TEXT";
        }

        var result = new StringBuilder();
        int index = 0;
        for (int line = 0; line < Lines && index < flat.Length; line++)
        {
            if (line > 0)
            {
                result.Append('\n');
            }

            int take = Math.Min(LineChars, flat.Length - index);
            if (index + take < flat.Length)
            {
                int breakAt = flat.LastIndexOf(' ', index + take - 1, take);
                if (breakAt > index)
                {
                    take = breakAt - index;
                }
            }

            result.Append(flat, index, take);
            index += take;
            while (index < flat.Length && flat[index] == ' ')
            {
                index++;
            }
        }

        return result.ToString();
    }

    private static string Clip(string? screenText)
    {
        string flat = Flatten(screenText);
        if (flat.Length <= MaxScreenChars)
        {
            return flat;
        }

        return flat[..MaxScreenChars];
    }

    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char character in text.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
