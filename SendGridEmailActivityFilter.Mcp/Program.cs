using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SendGridEmailActivityFilter.Core;
using SendGridEmailActivityFilter.Mcp.Tools;

// Use CreateApplicationBuilder (not CreateDefaultBuilder) for stdio MCP servers.
// CreateApplicationBuilder ensures no providers write to stdout before logging is configured.
var builder = Host.CreateApplicationBuilder(args);

// Clear all default logging providers — some write to stdout, which corrupts the JSON-RPC stream.
// Re-add Console directed entirely to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts =>
    opts.LogToStandardErrorThreshold = LogLevel.Trace);

var config = builder.Configuration;
var apiKey = config["SendGrid:ApiKey"]
    ?? throw new InvalidOperationException(
        "SendGrid:ApiKey missing from appsettings.json");
var limit = int.TryParse(config["SendGrid:Limit"], out var l) ? l : 20;

// SendGridService has primitive constructor parameters that DI cannot resolve automatically.
builder.Services.AddSingleton(_ => new SendGridService(new HttpClient(), apiKey, limit));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(EmailActivityTool).Assembly);

await builder.Build().RunAsync();
// The ModelContextProtocol stdio transport propagates EOF on stdin as host shutdown.
