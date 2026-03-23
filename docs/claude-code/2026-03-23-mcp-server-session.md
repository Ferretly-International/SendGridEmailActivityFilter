# Session: MCP Server Addition — 2026-03-23

## What we did

Added an MCP (Model Context Protocol) stdio server to the project so that Claude Chat can query SendGrid email activity via natural language (e.g. *"pull up the email logs for soandso@there.com for the last week"*), while keeping the existing interactive console app intact.

We also added an optional `days` filter to both the console app and the MCP tool, and set up the `appsettings.json` / `appsettings.example.json` gitignore pattern so API keys are never committed.

---

## How the session ran

The session used a structured brainstorm-first workflow:

1. **Brainstorming** — clarified transport (stdio), project structure (shared library vs. dual-mode vs. duplication), and feature scope (optional `days` parameter + `Limit` config ceiling).
2. **Design spec** — written to `docs/superpowers/specs/2026-03-23-mcp-server-design.md` and reviewed through four automated spec-review passes before a single line of code was written. Each pass caught real issues (wrong constructor pattern, missing package, stdout corruption risk, wrong `Hosting` version, missing `CopyToPublishDirectory`).
3. **Implementation** — five tasks executed sequentially via subagents, each followed by a spec-compliance review and a code-quality review.

---

## Key decisions

### 3-project solution (Option A — shared library)

The core SendGrid query logic lives in `SendGridEmailActivityFilter.Core` (a class library with no UI or MCP dependencies). Both executables reference it. This was chosen over a dual-mode single binary (Option B) specifically because stdio MCP servers have a hard constraint: any stray byte on stdout corrupts the JSON-RPC channel. Spectre.Console output and MCP output cannot safely share a process.

### `Host.CreateApplicationBuilder` instead of `CreateDefaultBuilder`

The official MCP C# SDK guidance is to use `CreateApplicationBuilder` for stdio servers. `CreateDefaultBuilder` registers logging providers before `ConfigureLogging` fires, which risks stdout writes before the channel is established. `CreateApplicationBuilder` gives you the fluent builder API (`builder.Logging`, `builder.Services`) where logging is configured before anything is registered.

### `new HttpClient()` directly (no `IHttpClientFactory`)

`SendGridService` takes `HttpClient`, `string apiKey`, and `int limit` as constructor parameters. The DI container can't resolve primitive types automatically, so a factory lambda is used:
```csharp
builder.Services.AddSingleton(_ => new SendGridService(new HttpClient(), apiKey, limit));
```
Avoiding `IHttpClientFactory` kept the MCP project free of a `Microsoft.Extensions.Http` reference.

### `appsettings.json` gitignore pattern

The root `.gitignore` uses three non-path-anchored rules that cover all projects:
- `appsettings.json` — ignores real config in any subdirectory
- `appsettings.*.json` — catches environment-specific overrides
- `!appsettings.example.json` — un-ignores the tracked template

No changes to `.gitignore` were needed when adding the MCP project's config files.

---

## What was built

### `SendGridEmailActivityFilter.Core`
- `Models.cs` — `EmailActivityResponse` and `Message` records (moved from the console app)
- `SendGridService.cs` — HTTP client wrapper; builds SGQL query with optional `last_event_time>TIMESTAMP` date filter; throws `HttpRequestException` on non-2xx; accepts `CancellationToken`

### `SendGridEmailActivityFilter` (console app — refactored)
- `Program.cs` — now uses `SendGridService` from Core; added `days` prompt using `TextPrompt.AllowEmpty()` (ordinary `Ask<string>` rejects blank input); `apiErrored` flag prevents the "no messages found" message showing after a real API error

### `SendGridEmailActivityFilter.Mcp` (new)
- `Program.cs` — Generic Host with stdio MCP transport; logging cleared and re-routed to stderr
- `Tools/EmailActivityTool.cs` — `[McpServerToolType]` class with one `[McpServerTool]` method returning a Markdown table; re-throws `OperationCanceledException` so the MCP framework handles client disconnects cleanly
- `appsettings.example.json` — tracked config template

---

## Spec review catches worth noting

The four automated spec review passes caught issues that would have caused real problems:

| Issue | Impact if missed |
|---|---|
| Duplicate constructor pattern (`primary ctor + explicit ctor with `: this()`) | Compile error |
| `services.AddHttpClient()` requires `Microsoft.Extensions.Http` not in the dep graph | Compile error |
| `Host.CreateDefaultBuilder` writes to stdout before logging config fires | JSON-RPC stream corruption at runtime |
| `Microsoft.Extensions.Hosting 8.*` conflicts with MCP package's `>= 10.0.3` floor | NuGet restore failure or runtime mismatch |
| `CopyToOutputDirectory` without `CopyToPublishDirectory` on `appsettings.json` | Config file missing from publish output; host throws on startup |

---

## Commit history

| SHA | Description |
|---|---|
| `9ab1411` | Initial commit: SendGrid Email Activity Filter console app |
| `c2d7d06` | Replace appsettings.json with example template; gitignore real config |
| `65a30e2` | Add MCP server design spec |
| `8c73417` | Update MCP spec: fix constructor, remove IHttpClientFactory, pin package version |
| `f6f90dc` | Spec: switch to CreateApplicationBuilder, fix Hosting version, add CopyToPublishDirectory |
| `7d5748c` | Scaffold solution file and add Core + Mcp projects |
| `698a095` | Add Core library Models and SendGridService |
| `e76d451` | Refactor console app to use Core library |
| `9a9e7b7` | Fix unused ctx parameter in Status lambda |
| `8d41aa7` | Implement MCP server and add CancellationToken support to SendGridService |
| `70beb4b` | Pass CancellationToken to ReadAsStringAsync; re-throw OperationCanceledException in tool |
| `6c3f5fa` | Update README: add MCP server setup and project structure |

---

## Related files

- Design spec: `docs/superpowers/specs/2026-03-23-mcp-server-design.md`
- README setup instructions: `README.md` → *MCP server setup*
