# FtrIO.OneTwo

A .NET CLI tool that scans a project directory for [FtrIO](https://github.com/TheScottBot/FtrIO) feature toggle usage and reports the current state of every toggle.

Because FtrIO always resolves toggle state from `appsettings.json` at runtime, FtrIO.OneTwo gives you an instant at-a-glance view of exactly what is enabled or disabled in your codebase right now — and precisely where each toggle is used — without having to open a single source file or config manually.

## What it does

FtrIO.OneTwo walks a project's source tree, finds every toggle reference, cross-references it against `appsettings.json`, and outputs a table showing the current state of each toggle.

It detects toggles from four patterns:

| Pattern | Use case |
|---|---|
| `[Toggle]` | Synchronous method gated by its own name |
| `[ToggleAsync]` | `Task`-returning method gated by its own name |
| `ExecuteMethodIfToggleOn(action, "key")` | Manual synchronous gating with an explicit key |
| `ExecuteMethodIfToggleOnAsync(func, "key")` | Manual async gating with an explicit key |

```csharp
// Attribute — toggle key is inferred from the method name
[Toggle]
public void SendWelcomeEmail() { }

[ToggleAsync]
public async Task SendNewsletterAsync() { }

// Manual call — toggle key is the string literal argument
featureToggle.ExecuteMethodIfToggleOn(ProcessOrder, "NewCheckoutFlow");
await featureToggle.ExecuteMethodIfToggleOnAsync(SyncDataAsync, "BetaSync");
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Installation

Pack and install as a global dotnet tool:

```bash
dotnet pack ./FtrIO.OneTwo
dotnet tool install -g FtrIO.OneTwo --add-source ./FtrIO.OneTwo/nupkg
```

## Usage

```
ftrio-onetwo [path] [--markdown <output.md>]
```

| Argument | Description |
|---|---|
| `path` | Path to the project or solution directory to scan. Defaults to the current directory. |
| `--markdown <file>` | Also write the results to a markdown file at the given path. |
| `--help` / `-h` | Show usage. |

**Examples:**

```bash
# Scan a project and print a table to the console
ftrio-onetwo C:\Projects\MyApp

# Also emit a markdown report
ftrio-onetwo C:\Projects\MyApp --markdown toggles.md

# Scan the current directory
ftrio-onetwo
```

## Example output

```
Scanning C:\Projects\MyApp...
╭──────────────────┬──────────────────┬────────────┬─────────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source     │  State  │ File              │ Line │
├──────────────────┼──────────────────┼────────────┼─────────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle]   │   20%   │ Services\Order.cs │    9 │
│ OldCheckoutFlow  │ OldCheckoutFlow  │ [Toggle]   │   OFF   │ Services\Order.cs │   14 │
│ PaymentV2        │ PaymentV2        │ [Toggle]   │  BLUE   │ Services\Pay.cs   │    6 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle]   │   ON    │ Services\Email.cs │   22 │
│ UnknownFeature   │ UnknownFeature   │ ManualCall │ MISSING │ Controllers\Ho... │   42 │
╰──────────────────┴──────────────────┴────────────┴─────────┴───────────────────┴──────╯

5 toggle(s) found. 1 ON, 1 OFF, 1 PERCENTAGE, 1 BLUE/GREEN, 1 MISSING from appsettings.
```

## States

| State | Meaning |
|---|---|
| `ON` | Toggle is `true` or `1` in `appsettings.json` |
| `OFF` | Toggle is `false` or `0` in `appsettings.json` |
| `20%` | Percentage rollout — the raw value (e.g. `"20%"`) is shown directly |
| `BLUE` / `GREEN` | Blue-green deployment slot — shown in uppercase |
| `MISSING` | Toggle key is used in code but has no entry in any `appsettings*.json` file |

## How toggle state is resolved

The tool searches for all `appsettings*.json` files under the scanned directory and reads the `Toggles` section. FtrIO supports boolean, percentage, and blue-green values in the same config file:

```json
{
  "Toggles": {
    "SendWelcomeEmail": true,
    "OldCheckoutFlow": false,
    "NewCheckoutFlow": "20%",
    "PaymentV2": "blue"
  }
}
```

This matches the configuration structure expected by [FtrIO](https://github.com/TheScottBot/FtrIO).

## Building from source

```bash
cd FtrIO.OneTwo
dotnet build
dotnet run -- <path>
```

## Related

- [FtrIO](https://github.com/TheScottBot/FtrIO) — the feature toggle library this tool supplements
