using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiKey = config["SendGrid:ApiKey"]
    ?? throw new InvalidOperationException("SendGrid:ApiKey is missing from appsettings.json");

AnsiConsole.Write(new FigletText("SendGrid Activity").Color(Color.CornflowerBlue));

var email = AnsiConsole.Ask<string>("[grey]Email address to query:[/] ");

if (string.IsNullOrWhiteSpace(email))
{
    AnsiConsole.MarkupLine("[red]No email address provided.[/]");
    return;
}

var limitStr = config["SendGrid:Limit"] ?? "20";
var limit = int.TryParse(limitStr, out var parsedLimit) ? parsedLimit : 20;

EmailActivityResponse? result = null;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("cornflowerblue"))
    .StartAsync($"Querying activity for [yellow]{Markup.Escape(email)}[/]...", async ctx =>
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var query = Uri.EscapeDataString($"to_email=\"{email}\"");
        var url = $"https://api.sendgrid.com/v3/messages?limit={limit}&query={query}";

        var response = await httpClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]API error {(int)response.StatusCode}:[/] {Markup.Escape(body)}");
            return;
        }

        result = JsonSerializer.Deserialize<EmailActivityResponse>(body);
    });

if (result?.Messages is not { Length: > 0 } messages)
{
    AnsiConsole.MarkupLine("[yellow]No messages found for that address.[/]");
    return;
}

AnsiConsole.MarkupLine($"\nFound [green]{messages.Length}[/] message(s) for [yellow]{Markup.Escape(email)}[/]:\n");

var table = new Table()
    .Border(TableBorder.Rounded)
    .BorderColor(Color.Grey)
    .AddColumn(new TableColumn("[bold]Date[/]").Centered())
    .AddColumn(new TableColumn("[bold]From[/]"))
    .AddColumn(new TableColumn("[bold]Subject[/]"))
    .AddColumn(new TableColumn("[bold]Status[/]").Centered())
    .AddColumn(new TableColumn("[bold]Opens[/]").Centered())
    .AddColumn(new TableColumn("[bold]Clicks[/]").Centered())
    .AddColumn(new TableColumn("[bold]Message ID[/]"));

foreach (var msg in messages)
{
    var statusMarkup = msg.Status?.ToLowerInvariant() switch
    {
        "delivered"    => "[green]delivered[/]",
        "opened"       => "[blue]opened[/]",
        "clicked"      => "[cyan]clicked[/]",
        "bounced"      => "[red]bounced[/]",
        "blocked"      => "[red]blocked[/]",
        "spam"         => "[orange3]spam[/]",
        "unsubscribed" => "[yellow]unsubscribed[/]",
        _              => Markup.Escape(msg.Status ?? "unknown")
    };

    var date = msg.LastEventTime is not null
        ? DateTime.TryParse(msg.LastEventTime, out var dt)
            ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : msg.LastEventTime
        : "";

    table.AddRow(
        date,
        Markup.Escape(msg.FromEmail ?? ""),
        Markup.Escape(msg.Subject ?? ""),
        statusMarkup,
        (msg.OpensCount ?? 0).ToString(),
        (msg.ClicksCount ?? 0).ToString(),
        Markup.Escape(msg.MsgId ?? "")
    );
}

AnsiConsole.Write(table);

record EmailActivityResponse(
    [property: JsonPropertyName("messages")] Message[]? Messages
);

record Message(
    [property: JsonPropertyName("msg_id")]          string? MsgId,
    [property: JsonPropertyName("from_email")]      string? FromEmail,
    [property: JsonPropertyName("to_email")]        string? ToEmail,
    [property: JsonPropertyName("subject")]         string? Subject,
    [property: JsonPropertyName("status")]          string? Status,
    [property: JsonPropertyName("opens_count")]     int?    OpensCount,
    [property: JsonPropertyName("clicks_count")]    int?    ClicksCount,
    [property: JsonPropertyName("last_event_time")] string? LastEventTime
);
