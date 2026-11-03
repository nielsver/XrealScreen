using XrealScreen.Core.Hud;

namespace XrealScreen.Core.Tests;

public class GrokCliAnswerTests
{
    [Fact]
    public void Text_ReadsTheJsonReply()
    {
        const string stdout = """
            {
              "text": "Open the build window.",
              "stopReason": "end_turn"
            }
            """;

        Assert.Equal("Open the build window.", GrokCliAnswer.Text(stdout));
    }

    [Fact]
    public void Text_ReadsTheLastStreamingResult()
    {
        const string stdout = """
            {"type":"assistant","message":{"content":[{"type":"text","text":"partial"}]}}
            {"type":"result","subtype":"success","result":"Klaar met de mail."}
            """;

        Assert.Equal("Klaar met de mail.", GrokCliAnswer.Text(stdout));
    }

    [Fact]
    public void Text_IgnoresAnErrorObject()
    {
        Assert.Null(GrokCliAnswer.Text("{\"type\":\"error\",\"message\":\"nope\"}"));
        Assert.Null(GrokCliAnswer.Text("{\"is_error\":true,\"text\":\"secret\"}"));
    }
}
