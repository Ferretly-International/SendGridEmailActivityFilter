# SendGrid Email Activity Filter

A .NET 8 console application and MCP server that query [SendGrid's Email Activity API](https://docs.sendgrid.com/api-reference/e-mail-activity/filter-messages-by-specific-parameters) — either for a specific recipient address or for all emails within a date range.

## Features

- Interactive ANSI console UI via **Spectre.Console**
- MCP server — ask Claude Chat "pull up the email logs for soandso@there.com for the last week"
- Query by recipient email (with optional N-day lookback), **or** retrieve all emails within a date range (max 5 days) — the two modes are mutually exclusive
- Displays results in a colour-coded table (status, opens, clicks, date)
- Configuration via `appsettings.json` (API key, result limit — gitignored, never committed)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A SendGrid account with **Email Activity** access enabled on your API key

### SendGrid API Key permissions required

When creating your API key in the SendGrid dashboard, enable:

- **Email Activity** → Full Access

> Note: The Email Activity API (`/v3/messages`) is available on paid SendGrid plans. Free plans may have limited or no access.

---

## Console app setup

1. Clone this repository.
2. Copy the example config and add your API key:

   ```bash
   cp SendGridEmailActivityFilter/appsettings.example.json SendGridEmailActivityFilter/appsettings.json
   ```

   Then edit `appsettings.json` and replace the placeholder:

   ```json
   {
     "SendGrid": {
       "ApiKey": "SG.xxxxxxxxxxxxxxxxxxxx",
       "Limit": 20
     }
   }
   ```

   > `appsettings.json` is listed in `.gitignore` and will never be committed. Only `appsettings.example.json` (with no real credentials) is tracked by Git.

3. Build and run:

   ```bash
   cd SendGridEmailActivityFilter
   dotnet run
   ```

---

## MCP server setup

The MCP server lets Claude Chat (or any MCP-compatible client) query email activity via natural language.

### 1. Configure

```bash
cp SendGridEmailActivityFilter.Mcp/appsettings.example.json SendGridEmailActivityFilter.Mcp/appsettings.json
```

Edit `SendGridEmailActivityFilter.Mcp/appsettings.json` and set your API key.

### 2. Publish (self-contained)

```bash
dotnet publish SendGridEmailActivityFilter.Mcp/SendGridEmailActivityFilter.Mcp.csproj -c Release -r win-x64 --self-contained
```

Self-contained publish is recommended so Claude Desktop can launch the executable without needing .NET on its PATH.

### 3. Copy config to publish output

After publishing, copy your config to the publish directory:

```bash
copy SendGridEmailActivityFilter.Mcp\appsettings.example.json <publish_output_dir>\appsettings.json
```

Then edit `<publish_output_dir>\appsettings.json` and set `SendGrid:ApiKey`.

### 4. Register with Claude Desktop

Add an entry to `claude_desktop_config.json` (find it via Claude Desktop → Settings → Developer):

```json
{
  "mcpServers": {
    "sendgrid-activity": {
      "command": "C:\\full\\path\\to\\publish\\sendgrid-mcp.exe"
    }
  }
}
```

Use a full absolute path. The executable is named `sendgrid-mcp.exe`.

Restart Claude Desktop. You can then ask things like:
- *"Pull up the email logs for user@example.com"*
- *"Show me SendGrid activity for user@example.com for the last 7 days"*
- *"Show me all emails sent between 2025-01-01 and 2025-01-05"*

---

## Configuration

Both the console app and MCP server use the same config keys:

| Key | Description | Default |
|-----|-------------|---------|
| `SendGrid:ApiKey` | Your SendGrid API key | *(required)* |
| `SendGrid:Limit` | Max number of messages to return (1–1000) | `20` |

---

## Usage (console)

```
Date filter: [No filter (most recent)] [Days to look back] [Date range]
```

Select a filter mode:

| Option | Behaviour |
|--------|-----------|
| **No filter** | Prompts for an email address; returns that recipient's most recent messages up to the configured limit |
| **Days to look back** | Prompts for an email address and a number of days; returns that recipient's messages since that many days ago |
| **Date range** | Prompts for a start and end date (`yyyy-MM-dd`, max 5 days); returns **all** emails in that period regardless of recipient — no email address required |

Results are displayed in a rounded table. The columns differ slightly by mode:

**Email / days lookback mode:**

| Column | Description |
|--------|-------------|
| Date | Last event timestamp (local time) |
| From | Sender address |
| Subject | Email subject line |
| Status | Colour-coded delivery status |
| Opens | Number of open events |
| Clicks | Number of click events |
| Message ID | SendGrid message identifier |

**Date range mode** (includes recipient column since results span multiple recipients):

| Column | Description |
|--------|-------------|
| Date | Last event timestamp (local time) |
| To | Recipient address |
| From | Sender address |
| Subject | Email subject line |
| Status | Colour-coded delivery status |
| Opens | Number of open events |
| Clicks | Number of click events |
| Message ID | SendGrid message identifier |

---

## Project structure

```
SendGridEmailActivityFilter/
├── SendGridEmailActivityFilter.sln
├── SendGridEmailActivityFilter.Core/        # Shared service + models
│   ├── SendGridService.cs
│   ├── Models.cs
│   └── SendGridEmailActivityFilter.Core.csproj
├── SendGridEmailActivityFilter/             # Console app
│   ├── Program.cs
│   ├── appsettings.example.json            # Config template (committed)
│   ├── appsettings.json                    # Your local config (gitignored)
│   └── SendGridEmailActivityFilter.csproj
├── SendGridEmailActivityFilter.Mcp/         # MCP server
│   ├── Program.cs
│   ├── Tools/EmailActivityTool.cs
│   ├── appsettings.example.json            # Config template (committed)
│   ├── appsettings.json                    # Your local config (gitignored)
│   └── SendGridEmailActivityFilter.Mcp.csproj
├── .gitignore
└── README.md
```
