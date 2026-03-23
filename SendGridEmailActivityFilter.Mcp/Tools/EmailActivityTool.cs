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
                 "open and click counts.")]
    public async Task<string> GetEmailActivity(
        [Description("Recipient email address to look up")] string email,
        [Description("Number of days to look back. Optional — omit for most recent messages up to the configured limit.")] int? days = null,
        CancellationToken cancellationToken = default)
    {
        EmailActivityResponse? result;
        try
        {
            result = await sendGrid.GetEmailActivityAsync(email, days, cancellationToken);
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
