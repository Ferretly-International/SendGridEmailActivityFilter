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

    /// <summary>
    /// Maximum allowed date range span when using startDate/endDate filtering.
    /// </summary>
    public static readonly TimeSpan MaxDateRangeSpan = TimeSpan.FromDays(5);

    public async Task<EmailActivityResponse?> GetEmailActivityAsync(
        string email,
        int? days = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (startDate.HasValue && endDate.HasValue)
        {
            var span = endDate.Value.Date - startDate.Value.Date;
            if (span < TimeSpan.Zero)
                throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
            if (span > MaxDateRangeSpan)
                throw new ArgumentException(
                    $"Date range cannot exceed {(int)MaxDateRangeSpan.TotalDays} days.", nameof(endDate));
        }

        var filter = $"to_email=\"{email}\"";

        if (startDate.HasValue && endDate.HasValue)
        {
            // Convert to UTC for SGQL query; use inclusive bounds covering full days
            var start = startDate.Value.Date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var end   = endDate.Value.Date.AddDays(1).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            filter += $" AND last_event_time>=TIMESTAMP \"{start}\"";
            filter += $" AND last_event_time<TIMESTAMP \"{end}\"";
        }
        else if (days.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days.Value)
                                        .ToString("yyyy-MM-dd HH:mm:ss");
            // SendGrid SGQL: > operator, TIMESTAMP keyword, "yyyy-MM-dd HH:mm:ss" format
            filter += $" AND last_event_time>TIMESTAMP \"{cutoff}\"";
        }

        var url = $"https://api.sendgrid.com/v3/messages" +
                  $"?limit={Math.Min(_limit, 1000)}" +
                  $"&query={Uri.EscapeDataString(filter)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"SendGrid API returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);

        return JsonSerializer.Deserialize<EmailActivityResponse>(body);
    }
}
