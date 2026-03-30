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

int? days = null;
DateTime? startDate = null;
DateTime? endDate = null;

var filterChoice = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("[grey]Date filter:[/]")
        .AddChoices("No filter (most recent)", "Days to look back", "Date range"));

string? email = null;

if (filterChoice == "Date range")
{
    var maxDays = (int)SendGridService.MaxDateRangeSpan.TotalDays;
    DateTime? parsedStart = null;
    DateTime? parsedEnd = null;

    while (parsedStart is null)
    {
        var input = AnsiConsole.Ask<string>("[grey]Start date (yyyy-MM-dd):[/] ");
        if (DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            parsedStart = d;
        else
            AnsiConsole.MarkupLine("[yellow]Invalid date — use yyyy-MM-dd format.[/]");
    }

    while (parsedEnd is null)
    {
        var input = AnsiConsole.Ask<string>("[grey]End date (yyyy-MM-dd):[/] ");
        if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
        {
            AnsiConsole.MarkupLine("[yellow]Invalid date — use yyyy-MM-dd format.[/]");
            continue;
        }
        if (d < parsedStart.Value)
        {
            AnsiConsole.MarkupLine("[yellow]End date must be on or after start date.[/]");
            continue;
        }
        var inclusiveDays = (d.Date - parsedStart.Value.Date).Days + 1;
        if (inclusiveDays > maxDays)
        {
            AnsiConsole.MarkupLine($"[yellow]Range cannot exceed {maxDays} days. Please enter an end date within {maxDays} days of {parsedStart.Value:yyyy-MM-dd}.[/]");
            continue;
        }
        parsedEnd = d;
    }

    startDate = parsedStart;
    endDate   = parsedEnd;
}
else
{
    // Email is required when not using a date range
    email = AnsiConsole.Ask<string>("[grey]Email address to query:[/] ");
    if (string.IsNullOrWhiteSpace(email))
    {
        AnsiConsole.MarkupLine("[red]No email address provided.[/]");
        return;
    }

    if (filterChoice == "Days to look back")
    {
        var daysInput = AnsiConsole.Prompt(
            new TextPrompt<string>("[grey]Days to look back:[/] ")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(daysInput))
        {
            if (int.TryParse(daysInput.Trim(), out var parsedDays) && parsedDays > 0)
                days = parsedDays;
            else
                AnsiConsole.MarkupLine("[yellow]Invalid days value — querying without date filter.[/]");
        }
    }
}

EmailActivityResponse? result = null;
var apiErrored = false;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("cornflowerblue"))
    .StartAsync($"Querying activity for [yellow]{Markup.Escape(email ?? "date range")}[/]...", async _ =>
    {
        try
        {
            using var httpClient = new HttpClient();
            var service = new SendGridService(httpClient, apiKey, limit);
            result = await service.GetEmailActivityAsync(email, days, startDate, endDate);
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
    AnsiConsole.MarkupLine("[yellow]No messages found.[/]");
    return;
}

var label = email is not null ? $"for [yellow]{Markup.Escape(email)}[/]" : "in date range";
var limitWarning = (startDate.HasValue && messages.Length == 1000)
    ? " [yellow](limit reached — there may be more)[/]"
    : string.Empty;
AnsiConsole.MarkupLine($"\nFound [green]{messages.Length}[/] message(s) {label}{limitWarning}:\n");

var isDateRange = startDate.HasValue;

var table = new Table()
    .Border(TableBorder.Rounded)
    .BorderColor(Color.Grey)
    .AddColumn(new TableColumn("[bold]Date[/]").Centered());

if (isDateRange)
    table.AddColumn(new TableColumn("[bold]To[/]"));

table
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

    var cells = new List<string> { date };
    if (isDateRange) cells.Add(Markup.Escape(msg.ToEmail ?? ""));
    cells.Add(Markup.Escape(msg.FromEmail ?? ""));
    cells.Add(Markup.Escape(msg.Subject ?? ""));
    cells.Add(statusMarkup);
    cells.Add((msg.OpensCount ?? 0).ToString());
    cells.Add((msg.ClicksCount ?? 0).ToString());
    cells.Add(Markup.Escape(msg.MsgId ?? ""));
    table.AddRow(cells.ToArray());
}

AnsiConsole.Write(table);
