using Microsoft.Extensions.Configuration;
using SendGridEmailActivityFilter.Core;
using Spectre.Console;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var apiKey = config["SendGrid:ApiKey"]
    ?? throw new InvalidOperationException("SendGrid:ApiKey is missing from appsettings.json");

var limitStr = config["SendGrid:Limit"] ?? "20";
var limit = int.TryParse(limitStr, out var parsedLimit) ? parsedLimit : 20;

AnsiConsole.Write(new FigletText("SendGrid Activity").Color(Color.CornflowerBlue));

var email = AnsiConsole.Ask<string>("[grey]Email address to query:[/] ");

if (string.IsNullOrWhiteSpace(email))
{
    AnsiConsole.MarkupLine("[red]No email address provided.[/]");
    return;
}

int? days = null;
// AnsiConsole.Ask<string> rejects empty input; TextPrompt with AllowEmpty() lets user press Enter to skip
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

EmailActivityResponse? result = null;
var apiErrored = false;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("cornflowerblue"))
    .StartAsync($"Querying activity for [yellow]{Markup.Escape(email)}[/]...", async _ =>
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
