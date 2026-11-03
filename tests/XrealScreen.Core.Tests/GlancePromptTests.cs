using System.Text.Json;
using XrealScreen.Core.Hud;

namespace XrealScreen.Core.Tests;

public class GlancePromptTests
{
    [Fact]
    public void Request_DoesNotStoreTheScreenText()
    {
        using JsonDocument document = JsonDocument.Parse(GlancePrompt.RequestJson("hello from the desk"));
        JsonElement root = document.RootElement;

        Assert.Equal(GlancePrompt.Model, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("hello from the desk", root.GetProperty("input")[1].GetProperty("content").GetString());
    }

    [Fact]
    public void Answer_ReadsTheOutputText()
    {
        const string body = """
            {
              "output": [
                { "type": "reasoning", "content": [ { "type": "summary_text", "text": "thinking" } ] },
                { "type": "message", "content": [ { "type": "output_text", "text": "Mail from Sam." } ] }
              ]
            }
            """;

        Assert.Equal("Mail from Sam.", GlancePrompt.Answer(body));
    }

    [Fact]
    public void ForHud_KeepsThreeShortLines()
    {
        string note = GlancePrompt.ForHud(string.Join(' ', Enumerable.Repeat("word", 80)));

        string[] lines = note.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.True(line.Length <= GlancePrompt.LineChars));
        Assert.Equal("NO TEXT", GlancePrompt.ForHud("   \n"));
    }
}
