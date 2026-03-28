using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SendGridEmailActivityFilter.Core;

namespace SendGridEmailActivityFilter.Mcp.Tools;

[McpServerToolType]
public class EmailActivityTool(SendGridService sendGrid)
{
    [McpServerTool]
    [Description("Query SendGrid email activity for a recipient email address. " +
                 "Returns a table of recent messages including delivery status, " +
                 "open and click counts. " +
                 "Provide either 'days' for a rolling lookback, or both 'startDate' and 'endDate' " +
                 "for a specific date range (maximum 5 days). Date range takes precedence when both are supplied.")]
    public async Task<string> GetEmailActivity(
        [Description("Recipient email address to look up")] string email,
        [Description("Number of days to look back. Optional — omit for most recent messages up to the configured limit.")] int? days = null,
        [Description("Start of date range to filter by, in yyyy-MM-dd format (e.g. '2025-01-01'). Must be used together with endDate. Range may not exceed 5 days.")] string? startDate = null,
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

            parsedStart = s;
            parsedEnd   = e;
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
            return $"No messages found for {email}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {messages.Length} message(s) for {email}:");
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
