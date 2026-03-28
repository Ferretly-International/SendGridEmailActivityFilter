using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SendGridEmailActivityFilter.Core;

namespace SendGridEmailActivityFilter.Mcp.Tools;

[McpServerToolType]
public class EmailActivityTool(SendGridService sendGrid)
{
    [McpServerTool]
    [Description("Query SendGrid email activity. " +
                 "Returns a table of messages including delivery status, open and click counts. " +
                 "Either provide an email address (optionally with a days lookback) to query a specific recipient, " +
                 "or provide startDate and endDate (maximum 5-day range) to retrieve all emails within a date range " +
                 "regardless of recipient. Date range and email are mutually exclusive.")]
    public async Task<string> GetEmailActivity(
        [Description("Recipient email address to look up. Required unless startDate and endDate are provided — date range queries return all emails in the range regardless of recipient.")] string? email = null,
        [Description("Number of days to look back. Optional — omit for most recent messages up to the configured limit.")] int? days = null,
        [Description("Start of date range to filter by, in yyyy-MM-dd format (e.g. '2025-01-01'). Must be used together with endDate. Range may not exceed 5 days. When specified, email is not required.")] string? startDate = null,
        [Description("End of date range to filter by, in yyyy-MM-dd format (e.g. '2025-01-05'). Must be used together with startDate. Range may not exceed 5 days.")] string? endDate = null,
        CancellationToken cancellationToken = default)
    {
        DateTime? parsedStart = null;
        DateTime? parsedEnd   = null;

        if (startDate is not null || endDate is not null)
        {
            if (startDate is null || endDate is null)
                return "Both startDate and endDate must be provided together for date range filtering.";

            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var s))
                return "Invalid startDate — expected yyyy-MM-dd format.";

            if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var e))
                return "Invalid endDate — expected yyyy-MM-dd format.";

            if (e < s)
                return "endDate must be on or after startDate.";

            var maxDays = (int)SendGridService.MaxDateRangeSpan.TotalDays;
            if ((e.Date - s.Date).Days + 1 > maxDays)
                return $"Date range cannot exceed {maxDays} days.";

            if (!string.IsNullOrWhiteSpace(email))
                return "Date range and email are mutually exclusive — provide one or the other.";

            if (days.HasValue)
                return "Date range and days lookback are mutually exclusive — provide one or the other.";

            parsedStart = s;
            parsedEnd   = e;
        }
        else if (string.IsNullOrWhiteSpace(email))
        {
            return "Either email or a date range (startDate + endDate) must be provided.";
        }

        EmailActivityResponse? result;
        try
        {
            result = await sendGrid.GetEmailActivityAsync(email, days, parsedStart, parsedEnd, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Let the MCP framework handle client disconnects
        }
        catch (HttpRequestException ex)
        {
            return $"Error querying SendGrid: {ex.Message}";
        }

        if (result?.Messages is not { Length: > 0 } messages)
            return email is not null ? $"No messages found for {email}." : "No messages found in that date range.";

        var sb = new StringBuilder();
        var label = email is not null ? $"for {email}" : "in date range";
        sb.AppendLine($"Found {messages.Length} message(s) {label}:");
        sb.AppendLine();
        sb.AppendLine("| Date | From | Subject | Status | Opens | Clicks | Message ID |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var msg in messages)
        {
            var date = msg.LastEventTime is not null
                       && DateTime.TryParse(msg.LastEventTime, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : msg.LastEventTime ?? "";

            sb.AppendLine(
                $"| {date} " +
                $"| {Escape(msg.FromEmail)} " +
                $"| {Escape(msg.Subject)} " +
                $"| {msg.Status ?? "unknown"} " +
                $"| {msg.OpensCount ?? 0} " +
                $"| {msg.ClicksCount ?? 0} " +
                $"| {Escape(msg.MsgId)} |");
        }

        return sb.ToString();
    }

    private static string Escape(string? s) => (s ?? "").Replace("|", "\\|");
}
