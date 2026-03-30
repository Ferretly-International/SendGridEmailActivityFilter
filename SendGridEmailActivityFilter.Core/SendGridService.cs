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
        string? email = null,
        int? days = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (startDate.HasValue != endDate.HasValue)
            throw new ArgumentException(
                "Both startDate and endDate must be provided together when filtering by date range.",
                startDate.HasValue ? nameof(endDate) : nameof(startDate));

        if (!startDate.HasValue && string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Either email or a date range must be provided.", nameof(email));

        if (startDate.HasValue && (!string.IsNullOrWhiteSpace(email) || days.HasValue))
            throw new ArgumentException(
                "Date range filtering is mutually exclusive with email and days — provide one or the other.",
                !string.IsNullOrWhiteSpace(email) ? nameof(email) : nameof(days));

        if (startDate.HasValue && endDate.HasValue)
        {
            var span = endDate.Value.Date - startDate.Value.Date;
            if (span < TimeSpan.Zero)
                throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
            // +1 converts exclusive span to inclusive day count (e.g. Jan 1–Jan 5 = 5 days)
            if (span.TotalDays + 1 > MaxDateRangeSpan.TotalDays)
                throw new ArgumentException(
                    $"Date range cannot exceed {(int)MaxDateRangeSpan.TotalDays} days.", nameof(endDate));
        }

        // Date range queries return all emails in the range; email filter is only applied otherwise
        var filter = startDate.HasValue
            ? string.Empty
            : $"to_email=\"{email}\"";

        if (startDate.HasValue && endDate.HasValue)
        {
            var startBound = DateTime.SpecifyKind(startDate.Value.Date.AddSeconds(-1), DateTimeKind.Utc);
            var endBound   = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            filter = $"last_event_time>TIMESTAMP \"{startBound:yyyy-MM-ddTHH:mm:ssZ}\" AND last_event_time<TIMESTAMP \"{endBound:yyyy-MM-ddTHH:mm:ssZ}\"";
        }
        else if (days.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days.Value).ToString("yyyy-MM-ddTHH:mm:ssZ");
            filter += $" AND last_event_time>TIMESTAMP \"{cutoff}\"";
        }

        var url = $"https://api.sendgrid.com/v3/messages" +
                  $"?limit={Math.Min(_limit, 1000)}" +
                  $"&query={Uri.EscapeDataString(filter)}";

        var httpResponse = await _httpClient.GetAsync(url, cancellationToken);
        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"SendGrid API returned {(int)httpResponse.StatusCode}: {body}",
                null,
                httpResponse.StatusCode);

        return JsonSerializer.Deserialize<EmailActivityResponse>(body);
    }
}
