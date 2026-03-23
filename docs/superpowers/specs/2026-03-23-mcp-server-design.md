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
- Exposing `limit` as a tool parameter (it remains an operational config ceiling)

---

## Solution Structure

```
SendGridEmailActivityFilter/          ← repo root
├── SendGridEmailActivityFilter.sln
├── SendGridEmailActivityFilter.Core/
│   ├── SendGridService.cs
│   ├── Models.cs
│   └── SendGridEmailActivityFilter.Core.csproj
├── SendGridEmailActivityFilter/
│   ├── Program.cs
│   ├── appsettings.json              (gitignored)
│   ├── appsettings.example.json
│   └── SendGridEmailActivityFilter.csproj
└── SendGridEmailActivityFilter.Mcp/
    ├── Program.cs
    ├── Tools/EmailActivityTool.cs
    ├── appsettings.json              (gitignored)
    ├── appsettings.example.json
    └── SendGridEmailActivityFilter.Mcp.csproj
```

### Solution scaffolding commands

All commands run from the **repo root** (`SendGridEmailActivityFilter/` in the file system):

```bash
dotnet new sln
dotnet sln add SendGridEmailActivityFilter/SendGridEmailActivityFilter.csproj
dotnet new classlib -n SendGridEmailActivityFilter.Core
dotnet sln add SendGridEmailActivityFilter.Core/SendGridEmailActivityFilter.Core.csproj
dotnet new console -n SendGridEmailActivityFilter.Mcp
dotnet sln add SendGridEmailActivityFilter.Mcp/SendGridEmailActivityFilter.Mcp.csproj
```

`dotnet new classlib` and `dotnet new console` run from the repo root, producing sibling directories alongside the existing console app folder — matching the diagram above.

---

## Section 1 — Core Library (`SendGridEmailActivityFilter.Core`)

### Purpose
Houses all SendGrid API interaction and shared models. Has no console, MCP, or configuration-framework dependencies.

### `SendGridEmailActivityFilter.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`System.Net.Http` and `System.Text.Json` are inbox in .NET 8 — no NuGet references needed.

### `Models.cs`

Namespace: `SendGridEmailActivityFilter.Core`

`System.Text.Json.Serialization` is **not** in the default implicit usings for a class library and must be added explicitly.

```csharp
using System.Text.Json.Serialization;

namespace SendGridEmailActivityFilter.Core;

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

### `SendGridService.cs`

Namespace: `SendGridEmailActivityFilter.Core`

```csharp
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
        // Set headers once on the shared instance; both callers reuse this HttpClient.
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
            // SendGrid SGQL supports > (strictly greater-than) for last_event_time.
            // The TIMESTAMP keyword and "yyyy-MM-dd HH:mm:ss" format are required.
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
```

**Error contract:** `GetEmailActivityAsync` throws `HttpRequestException` (with status code and body in the message) on any non-2xx response. Callers are responsible for catching and surfacing this to the user.

**Header placement:** Both `Authorization` and `Accept` headers are set on `httpClient.DefaultRequestHeaders` in the constructor. The `HttpClient` instance is shared (singleton in MCP, `using`-scoped in console) so this is safe — the headers are set once and reused for every call.

---

## Section 2 — MCP Server (`SendGridEmailActivityFilter.Mcp`)

### NuGet packages

| Package | Purpose |
|---|---|
| `ModelContextProtocol` | Core MCP types, tool attributes (`[McpServerToolType]`, `[McpServerTool]`), stdio transport, and DI extensions (`AddMcpServer`, `WithStdioServerTransport`, `WithToolsFromAssembly`) |
| `Microsoft.Extensions.Hosting` | Generic Host |

> **Version note:** As of this spec's date the latest stable release is `ModelContextProtocol` `1.1.0`. Pin to `1.1.0` explicitly (not a `1.*` wildcard) since the SDK has had breaking API changes between releases. Check NuGet for a newer stable release before implementing.
>
> `Microsoft.Extensions.Hosting` must be referenced **explicitly** — `ModelContextProtocol 1.1.0` only brings in `Microsoft.Extensions.Hosting.Abstractions` (interfaces) transitively, not the full runtime package. `Microsoft.Extensions.Hosting` requires `Abstractions >= 10.0.3` from the MCP package, so pin `Hosting` to `10.*` (not `8.*`). `Microsoft.Extensions.Configuration.Json` is pulled in transitively via `Microsoft.Extensions.Hosting`'s own dependency chain and does not need a separate reference.

Project reference: `SendGridEmailActivityFilter.Core`

### `SendGridEmailActivityFilter.Mcp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>sendgrid-mcp</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.1.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SendGridEmailActivityFilter.Core\SendGridEmailActivityFilter.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
    <None Update="appsettings.example.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Project>
```

`<AssemblyName>sendgrid-mcp</AssemblyName>` produces `sendgrid-mcp.exe`, distinct from the console app's `SendGridEmailActivityFilter.exe`.

### `Program.cs`

> **Important:** Do NOT use `Host.CreateDefaultBuilder` for an MCP stdio server. The official MCP C# SDK guidance requires `Host.CreateApplicationBuilder` (or `Host.CreateEmptyApplicationBuilder`) so that no default providers write to stdout before the logging configuration is applied. `CreateDefaultBuilder` uses a callback-based API where providers can be registered before `ConfigureLogging` fires, risking stdout pollution that corrupts the JSON-RPC channel.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SendGridEmailActivityFilter.Core;
using SendGridEmailActivityFilter.Mcp.Tools;

// Use CreateApplicationBuilder (not CreateDefaultBuilder) for stdio MCP servers.
// CreateApplicationBuilder uses the newer HostApplicationBuilder fluent API and
// ensures no providers are registered before we configure logging below.
var builder = Host.CreateApplicationBuilder(args);
// appsettings.json and appsettings.{Environment}.json are loaded automatically.
// DOTNET_ENVIRONMENT defaults to "Production" for published executables; only
// machines with DOTNET_ENVIRONMENT=Development set will load appsettings.Development.json.
// The existing .gitignore rule appsettings.*.json covers that file if it is created.

// Clear all default logging providers (some write to stdout) and re-add Console
// directed entirely to stderr. LogToStandardErrorThreshold = Trace routes all
// log levels to stderr, keeping stdout exclusively for JSON-RPC messages.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts =>
    opts.LogToStandardErrorThreshold = LogLevel.Trace);

var config = builder.Configuration;
var apiKey = config["SendGrid:ApiKey"]
    ?? throw new InvalidOperationException(
        "SendGrid:ApiKey missing from appsettings.json");
var limit = int.TryParse(config["SendGrid:Limit"], out var l) ? l : 20;

// SendGridService has primitive constructor parameters (string, int) that the
// DI container cannot resolve automatically; use a factory lambda.
// Construct HttpClient directly — no IHttpClientFactory/Microsoft.Extensions.Http needed.
builder.Services.AddSingleton(_ => new SendGridService(new HttpClient(), apiKey, limit));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // Explicit assembly reference makes the tool scan self-documenting and
    // independent of the calling-assembly context at the call site.
    .WithToolsFromAssembly(typeof(EmailActivityTool).Assembly);

await builder.Build().RunAsync();
// The ModelContextProtocol stdio transport propagates EOF on stdin as a host
// shutdown signal; no additional CancellationToken wiring is required.
```

### `Tools/EmailActivityTool.cs`

Namespace: `SendGridEmailActivityFilter.Mcp.Tools`

```csharp
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
        [Description("Number of days to look back. Optional — omit for most recent messages up to the configured limit.")] int? days = null)
    {
        EmailActivityResponse? result;
        try
        {
            result = await sendGrid.GetEmailActivityAsync(email, days);
        }
        catch (HttpRequestException ex)
        {
            return $"Error querying SendGrid: {ex.Message}";
        }

        if (result?.Messages is not { Length: > 0 } messages)
            return $"No messages found for {email}.";

        // Markdown table — plain text only, no ANSI colour codes.
        // Columns: Date, From, Subject, Status, Opens, Clicks, Message ID
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

    // Escape pipe characters to avoid breaking the Markdown table structure.
    private static string Escape(string? s) => (s ?? "").Replace("|", "\\|");
}
```

### `appsettings.example.json`

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

### `.gitignore` coverage

The existing root `.gitignore` has three relevant rules (non-path-anchored, so they match in any subdirectory):
- `appsettings.json` — ignores both new `appsettings.json` files
- `appsettings.*.json` — would also catch `appsettings.example.json` and `appsettings.Development.json`
- `!appsettings.example.json` — un-ignores `appsettings.example.json` in all subdirectories

Net result: `appsettings.json` and `appsettings.Development.json` are ignored; `appsettings.example.json` is tracked. **No `.gitignore` changes are required.**

---

## Section 3 — Console App Changes

### `SendGridEmailActivityFilter.csproj` — updated `<ItemGroup>` blocks

```xml
<ItemGroup>
  <!-- Version 10.0.4 carried forward from existing project (targets net8.0; works via
       backward compatibility but consider downgrading to 8.* in a future cleanup). -->
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.4" />
  <PackageReference Include="Spectre.Console" Version="0.54.0" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\SendGridEmailActivityFilter.Core\SendGridEmailActivityFilter.Core.csproj" />
</ItemGroup>

<ItemGroup>
  <None Update="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

`Microsoft.Extensions.Configuration.Json` stays in the console project. It is **not** added to Core.

### `Program.cs` — key changes

**Remove these `using` directives** (no longer used after the refactor):
- `using System.Net.Http.Headers;`
- `using System.Text.Json;`
- `using System.Text.Json.Serialization;`

**Add at the top:**
```csharp
using SendGridEmailActivityFilter.Core;
```

**Remove from `Program.cs`:**
- The inline `HttpClient` block and query-building code
- The `EmailActivityResponse` and `Message` record definitions at the bottom

**Add after the email prompt** (days prompt):

```csharp
int? days = null;
// AnsiConsole.Ask<string> rejects empty input; use TextPrompt with AllowEmpty()
// so the user can press Enter to skip the date filter.
var daysInput = AnsiConsole.Prompt(
    new TextPrompt<string>("[grey]Days to look back (leave blank for all recent):[/] ")
        .AllowEmpty());
if (!string.IsNullOrWhiteSpace(daysInput))
{
    if (int.TryParse(daysInput.Trim(), out var parsedDays) && parsedDays > 0)
        days = parsedDays;
    else
        AnsiConsole.MarkupLine("[yellow]Invalid days value — querying without date filter.[/]");
}
```

**Replace the inline HTTP block with:**

```csharp
EmailActivityResponse? result = null;
var apiErrored = false;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("cornflowerblue"))
    .StartAsync($"Querying activity for [yellow]{Markup.Escape(email)}[/]...", async ctx =>
    {
        try
        {
            using var httpClient = new HttpClient();
            var service = new SendGridService(httpClient, apiKey, limit);
            result = await service.GetEmailActivityAsync(email, days);
        }
        catch (HttpRequestException ex)
        {
            apiErrored = true;
            AnsiConsole.MarkupLine($"[red]API error:[/] {Markup.Escape(ex.Message)}");
        }
    });

if (apiErrored) return;
```

The `apiErrored` flag prevents falling through to the "no messages found" yellow message after a real API error, which would be misleading.

**`days` validation:** blank → `null` (no date filter, preserves existing behaviour); non-blank but unparseable or `<= 0` → warn and fall back to `null`. No re-prompting.

The table rendering code is unchanged.

---

## Error Handling

| Scenario | Console app | MCP tool |
|---|---|---|
| API key missing | `InvalidOperationException` at startup | Host fails to start |
| Non-2xx from SendGrid | `HttpRequestException` caught, red error markup, early return via `apiErrored` flag | `HttpRequestException` caught, returns error string |
| No messages found | Yellow "no messages" message | Returns `"No messages found for <email>."` |
| `days` blank | `null` passed — no date filter | N/A (optional parameter defaults to `null`) |
| `days` unparseable / ≤ 0 | Warning, falls back to `null` | MCP SDK validates `int?` at protocol level |

---

## Deployment & Registration with Claude

### Build

```bash
# From repo root:
dotnet publish SendGridEmailActivityFilter.Mcp -c Release -r win-x64 --self-contained
```

Self-contained publish is recommended: the Claude Desktop process may not inherit the user's PATH or have .NET 8 available as a runtime prerequisite.

### Post-publish config setup

After publishing, copy the example config into the publish output directory and populate your API key:

```bash
copy SendGridEmailActivityFilter.Mcp\appsettings.example.json <publish_output_dir>\appsettings.json
```

Then edit `<publish_output_dir>\appsettings.json` and set `SendGrid:ApiKey`. This file is separate from the project-level `appsettings.json` used during `dotnet run`; both must exist and contain a valid key. The `CopyToPublishDirectory` directive in the `.csproj` copies the placeholder `appsettings.example.json` to the publish directory — rename/overwrite it with your real config.

### Claude Desktop config (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "sendgrid-activity": {
      "command": "C:\\full\\path\\to\\publish\\sendgrid-mcp.exe"
    }
  }
}
```

Use a full absolute path. The executable is named `sendgrid-mcp.exe` (via `<AssemblyName>`).

This should be documented in `README.md`.

---

## Out of Scope

- Unit tests (no testing framework exists in this repo today)
- Pagination beyond the `Limit` ceiling
- Additional MCP tools (e.g., get message detail by ID)
- Linux/macOS deployment (Windows-first project)
