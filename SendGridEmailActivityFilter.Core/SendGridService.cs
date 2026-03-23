using System.Net.Http.Headers;
using System.Text.Json;

namespace SendGridEmailActivityFilter.Core;

public class SendGridService
{
    private readonly HttpClient _httpClient;
    private readonly int _limit;

    public SendGridService(HttpClient httpClient, string apiKey, int limit)
    {
        _httpClient = httpClient;
        _limit = limit;
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<EmailActivityResponse?> GetEmailActivityAsync(
        string email, int? days = null)
    {
        var filter = $"to_email=\"{email}\"";
        if (days.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days.Value)
                                        .ToString("yyyy-MM-dd HH:mm:ss");
            // SendGrid SGQL: > operator, TIMESTAMP keyword, "yyyy-MM-dd HH:mm:ss" format
            filter += $" AND last_event_time>TIMESTAMP \"{cutoff}\"";
        }

        var url = $"https://api.sendgrid.com/v3/messages" +
                  $"?limit={Math.Min(_limit, 1000)}" +
                  $"&query={Uri.EscapeDataString(filter)}";

        var response = await _httpClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"SendGrid API returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);

        return JsonSerializer.Deserialize<EmailActivityResponse>(body);
    }
}
