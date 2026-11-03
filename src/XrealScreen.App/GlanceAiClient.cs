using System.Net.Http.Headers;
using System.Text;
using XrealScreen.Core.Hud;

namespace XrealScreen.App;

/// <summary>Sends recognized screen text to Grok. The key stays in the environment. Nothing is stored server-side.</summary>
internal static class GlanceAiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<string?> CompleteAsync(string apiKey, string screenText, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(GlancePrompt.RequestJson(screenText), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Screen note failed ({(int)response.StatusCode}).");
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return GlancePrompt.Answer(body);
    }
}
