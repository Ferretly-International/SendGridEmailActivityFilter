# SendGrid Email Activity Filter

A .NET 8 console application that queries [SendGrid's Email Activity API](https://docs.sendgrid.com/api-reference/e-mail-activity/filter-messages-by-specific-parameters) for a given recipient address and displays the results in a formatted table.

## Features

- Interactive ANSI console UI via **Spectre.Console**
- Queries SendGrid's `/v3/messages` endpoint filtered by `to_email`
- Displays results in a colour-coded table (status, opens, clicks, date)
- Configuration via `appsettings.json` (API key, result limit)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A SendGrid account with **Email Activity** access enabled on your API key

### SendGrid API Key permissions required

When creating your API key in the SendGrid dashboard, enable:

- **Email Activity** → Full Access

> Note: The Email Activity API (`/v3/messages`) is available on paid SendGrid plans. Free plans may have limited or no access.

## Setup

1. Clone this repository.
2. Open `SendGridEmailActivityFilter/appsettings.json` and replace the placeholder with your API key:

   ```json
   {
     "SendGrid": {
       "ApiKey": "SG.xxxxxxxxxxxxxxxxxxxx",
       "Limit": 20
     }
   }
   ```

3. Build and run:

   ```bash
   cd SendGridEmailActivityFilter
   dotnet run
   ```

## Configuration

| Key | Description | Default |
|-----|-------------|---------|
| `SendGrid:ApiKey` | Your SendGrid API key | *(required)* |
| `SendGrid:Limit` | Max number of messages to return (1–1000) | `20` |

## Usage

```
 ____                    _  ____      _     _      _        _   _       _ _
/ ___|  ___ _ __   __ _| |/ ___|_ __(_) __| |    / \   ___| |_(_)_   _(_) |_ _   _
\___ \ / _ \ '_ \ / _` | | |  _| '__| |/ _` |   / _ \ / __| __| \ \ / / | __| | | |
 ___) |  __/ | | | (_| | | |_| | |  | | (_| |  / ___ \ (__| |_| |\ V /| | |_| |_| |
|____/ \___|_| |_|\__,_|_|\____|_|  |_|\__,_| /_/   \_\___|\__|_| \_/ |_|\__|\__, |
                                                                               |___/

Email address to query: user@example.com
```

Results are displayed in a rounded table with columns:

| Column | Description |
|--------|-------------|
| Date | Last event timestamp (local time) |
| From | Sender address |
| Subject | Email subject line |
| Status | Colour-coded delivery status |
| Opens | Number of open events |
| Clicks | Number of click events |
| Message ID | SendGrid message identifier |

## Project structure

```
SendGridEmailActivityFilter/
├── SendGridEmailActivityFilter/
│   ├── Program.cs           # Application entry point
│   ├── appsettings.json     # Configuration (add your API key here)
│   └── SendGridEmailActivityFilter.csproj
├── .gitignore
└── README.md
```
