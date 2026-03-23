# MCP Server for SendGrid Email Activity — Design Spec

**Date:** 2026-03-23
**Status:** Approved

## Overview

Add a Model Context Protocol (MCP) stdio server so that Claude Chat (or any MCP-compatible client) can query SendGrid email activity by natural-language requests such as "pull up the email logs for soandso@there.com for the last week." The existing interactive console app is preserved unchanged in user-facing behaviour.

## Goals

- Expose a `get_email_activity` MCP tool callable from Claude Chat
- Run as a local stdio process (registered in Claude Desktop / Claude Chat MCP config)
- Support optional date-range filtering (`days` parameter) in both the MCP tool and the console app
- Share SendGrid query logic between both executables — no duplication

## Non-Goals

- HTTP/SSE transport (stdio only)
- Authentication beyond the existing API key in `appsettings.json`
- Exposing limit as a tool parameter (it remains an operational config ceiling)

---

## Solution Structure

```
SendGridEmailActivityFilter/
├── SendGridEmailActivityFilter.sln
├── SendGridEmailActivityFilter.Core/
│   ├── SendGridService.cs
│   ├── Models.cs
│   └── SendGridEmailActivityFilter.Core.csproj
├── SendGridEmailActivityFilter/               (existing console app)
│   ├── Program.cs
│   ├── appsettings.json                       (gitignored)
│   ├── appsettings.example.json
│   └── SendGridEmailActivityFilter.csproj
└── SendGridEmailActivityFilter.Mcp/           (new)
    ├── Program.cs
    ├── Tools/EmailActivityTool.cs
    ├── appsettings.json                       (gitignored)
    ├── appsettings.example.json
    └── SendGridEmailActivityFilter.Mcp.csproj
```

---

## Section 1 — Core Library (`SendGridEmailActivityFilter.Core`)

### Purpose
Houses all SendGrid API interaction and shared models. Has no console or MCP dependencies.

### Dependencies
- `System.Net.Http` (inbox)
- `System.Text.Json` (inbox)

### Models.cs
Move the two records from the console app's `Program.cs`:

```csharp
public record EmailActivityResponse(
    [property: JsonPropertyName("messages")] Message[]? Messages
);

public record Message(
    [property: JsonPropertyName("msg_id")]          string? MsgId,
    [property: JsonPropertyName("from_email")]      string? FromEmail,
    [property: JsonPropertyName("to_email")]        string? ToEmail,
    [property: JsonPropertyName("subject")]         string? Subject,
    [property: JsonPropertyName("status")]          string? Status,
    [property: JsonPropertyName("opens_count")]     int?    OpensCount,
    [property: JsonPropertyName("clicks_count")]    int?    ClicksCount,
    [property: JsonPropertyName("last_event_time")] string? LastEventTime
);
```

### SendGridService.cs

```csharp
public class SendGridService(HttpClient httpClient, string apiKey, int limit)
{
    public async Task<EmailActivityResponse?> GetEmailActivityAsync(
        string email, int? days = null) { ... }
}
```

**Query building:**
- Base filter: `to_email="<email>"`
- If `days` is provided: append `AND last_event_time>="<UTC ISO8601>"` using `DateTime.UtcNow.AddDays(-days.Value)`
- `limit` passed as query-string parameter (max 1000 per SendGrid API)
- `Authorization: Bearer <apiKey>` header

The `HttpClient` is injected so each caller controls its lifetime.

---

## Section 2 — MCP Server (`SendGridEmailActivityFilter.Mcp`)

### Dependencies
- `ModelContextProtocol` (official C# SDK, 1.x)
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Configuration.Json`
- `SendGridEmailActivityFilter.Core` (project reference)

### Program.cs
Uses .NET Generic Host with stdio transport:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(b => b.AddJsonFile("appsettings.json", optional: false))
    .ConfigureServices((ctx, services) =>
    {
        // Register HttpClient + SendGridService
        services.AddHttpClient<SendGridService>(...);
        services.AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();
    })
    .Build();

await host.RunAsync();
```

Logging is directed to stderr (Generic Host default when stdout is the MCP transport) so it never corrupts the JSON-RPC stream.

### Tools/EmailActivityTool.cs

```csharp
[McpServerToolType]
public class EmailActivityTool(SendGridService sendGrid)
{
    [McpServerTool]
    [Description("Query SendGrid email activity for a recipient address.")]
    public async Task<string> GetEmailActivity(
        [Description("Recipient email address to look up")] string email,
        [Description("Number of days to look back (optional; omit for all recent)")] int? days = null)
    {
        var result = await sendGrid.GetEmailActivityAsync(email, days);
        // format as Markdown table and return
    }
}
```

**Return format:** Markdown table (renders cleanly in Claude Chat; no additional formatting step needed by the model).

### appsettings.example.json
```json
{
  "SendGrid": {
    "ApiKey": "SG.xxxxxxxxxxxxxxxxxxxx",
    "Limit": 20
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

---

## Section 3 — Console App Changes

The console app is refactored to use `SendGridService` from Core. Changes are minimal:

- Remove inline `HttpClient` block, query-building logic, and the two record definitions
- Add project reference to `SendGridEmailActivityFilter.Core`
- Instantiate `SendGridService` directly (no DI host):
  ```csharp
  using var httpClient = new HttpClient();
  var service = new SendGridService(httpClient, apiKey, limit);
  var result = await service.GetEmailActivityAsync(email, days);
  ```
- Add optional `days` prompt after the email prompt:
  > `Days to look back (leave blank for all recent):`
  Blank input → `null` → no date filter (preserves existing behaviour)
- Table rendering code is unchanged

---

## Error Handling

| Scenario | Console app | MCP tool |
|---|---|---|
| API key missing | Throws `InvalidOperationException` at startup | Same — fails fast at host startup |
| Non-2xx from SendGrid | Prints red error markup | Returns error string to Claude |
| No messages found | Yellow "no messages" message | Returns "No messages found for `<email>`." string |
| Invalid `days` value | Prompt re-asks / ignores invalid input | Parameter is typed `int?` — MCP SDK validates |

---

## Registration with Claude

After building the MCP project, the user adds an entry to their Claude Desktop / Claude Chat MCP config:

```json
{
  "mcpServers": {
    "sendgrid-activity": {
      "command": "path/to/SendGridEmailActivityFilter.Mcp.exe"
    }
  }
}
```

This should be documented in the README.

---

## Out of Scope

- Unit tests (no testing framework exists in this repo today)
- Pagination beyond the `Limit` ceiling
- Additional MCP tools (e.g., get message detail by ID)
