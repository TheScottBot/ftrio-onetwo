# FtrIO.OneTwo

A .NET CLI tool that scans a project directory for [FtrIO](https://github.com/FtrOnOff/FtrIO) feature toggle usage and reports the current state of every toggle.

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

## The FtrIO ecosystem

- [**FtrIO**](https://github.com/FtrOnOff/FtrIO) — the core library. Weaves `[Toggle]` into your IL at compile time, reads state from `appsettings.json` at runtime, and optionally syncs from remote sources via the provider pipeline.
- [**FtrIO.Toaster**](https://github.com/FtrOnOff/FtrIO.Toaster) — a lightweight web UI for managing toggles live. Writes values through `ToggleProviderBuffer` so changes flush to `appsettings.json` and are picked up instantly via `ReloadOnChange` — no file editing, no restart.
- [**ftrio-onetwo**](https://github.com/FtrOnOff/FtrIO.onetwo) — a .NET CLI audit tool. Scans your source tree for every toggle reference, cross-references against `appsettings.json`, and reports each toggle's state (`ON` / `OFF` / `20%` / `BLUE` / `MISSING`) with file and line number.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) 6, 8, or 10 (net6.0, net8.0, and net10.0 are all supported)

## Installation

Pack and install as a global dotnet tool:

```bash
dotnet pack ./FtrIO.OneTwo
dotnet tool install -g FtrIO.OneTwo --add-source ./FtrIO.OneTwo/nupkg
```

## Usage

```
ftrio-onetwo [--source <path>] [--config <path>] [--env <name>] [--markdown <output.md>]
```

| Argument | Description |
|---|---|
| `--source <path>` | Directory to scan for toggle usage in `.cs` files. Defaults to the current directory. |
| `--config <path>` | Directory to search for `appsettings*.json` files. Defaults to `--source` when not specified. |
| `--env <name>` | Show a single environment using the base+overlay model (e.g. `--env Staging`). Omit to show all `appsettings` files as separate tables. |
| `--markdown <file>` | Also write the results to a markdown file at the given path. |
| `--help` / `-h` | Show usage. |

`--source` and `--config` can also be passed as positional arguments — the first positional value is the source path, the second is the config path.

**Examples:**

```bash
# Scan a project — source and config in the same directory
ftrio-onetwo --source C:\Projects\MyApp

# Source code and config files in separate locations
ftrio-onetwo --source C:\Projects\MyApp --config C:\Projects\MyApp\bin\Debug\net10.0

# Positional shorthand (source then config)
ftrio-onetwo "C:\Projects\MyApp" "C:\Server\configs"

# Explicitly scan against the Staging overlay
ftrio-onetwo --source C:\Projects\MyApp --env Staging

# Also emit a markdown report
ftrio-onetwo --source C:\Projects\MyApp --config C:\Server\configs --env Production --markdown toggles.md

# Scan the current directory
ftrio-onetwo
```

## Example output

Without `--env`, each `appsettings*.json` file found is shown as a separate table:

```
Scanning C:\Projects\MyApp...

── Development C:\Projects\MyApp\appsettings.Development.json
╭──────────────────┬──────────────────┬──────────┬───────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source   │ State │ File              │ Line │
├──────────────────┼──────────────────┼──────────┼───────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle] │  80%  │ Services\Order.cs │    9 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle] │  ON   │ Services\Email.cs │   22 │
╰──────────────────┴──────────────────┴──────────┴───────┴───────────────────┴──────╯
2 toggle(s). 1 ON, 0 OFF, 1 PERCENTAGE, 0 BLUE/GREEN, 0 MISSING.

── appsettings.json C:\Projects\MyApp\appsettings.json
╭──────────────────┬──────────────────┬──────────┬─────────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source   │  State  │ File              │ Line │
├──────────────────┼──────────────────┼──────────┼─────────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle] │   OFF   │ Services\Order.cs │    9 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle] │   ON    │ Services\Email.cs │   22 │
╰──────────────────┴──────────────────┴──────────┴─────────┴───────────────────┴──────╯
2 toggle(s). 1 ON, 1 OFF, 0 PERCENTAGE, 0 BLUE/GREEN, 0 MISSING.

── Staging C:\Projects\MyApp\appsettings.Staging.json
╭──────────────────┬──────────────────┬────────────┬─────────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source     │  State  │ File              │ Line │
├──────────────────┼──────────────────┼────────────┼─────────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle]   │   50%   │ Services\Order.cs │    9 │
│ PaymentV2        │ PaymentV2        │ [Toggle]   │  BLUE   │ Services\Pay.cs   │    6 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle]   │   ON    │ Services\Email.cs │   22 │
│ UnknownFeature   │ UnknownFeature   │ ManualCall │ MISSING │ Controllers\Ho... │   42 │
╰──────────────────┴──────────────────┴────────────┴─────────┴───────────────────┴──────╯
4 toggle(s). 1 ON, 0 OFF, 1 PERCENTAGE, 1 BLUE/GREEN, 1 MISSING.
```

With `--env`, a single table is shown for that environment alongside its file path:

```
Scanning C:\Projects\MyApp...

── Staging C:\Projects\MyApp\appsettings.Staging.json
╭──────────────────┬──────────────────┬────────────┬─────────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source     │  State  │ File              │ Line │
├──────────────────┼──────────────────┼────────────┼─────────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle]   │   50%   │ Services\Order.cs │    9 │
│ PaymentV2        │ PaymentV2        │ [Toggle]   │  BLUE   │ Services\Pay.cs   │    6 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle]   │   ON    │ Services\Email.cs │   22 │
│ UnknownFeature   │ UnknownFeature   │ ManualCall │ MISSING │ Controllers\Ho... │   42 │
╰──────────────────┴──────────────────┴────────────┴─────────┴───────────────────┴──────╯
4 toggle(s). 1 ON, 0 OFF, 1 PERCENTAGE, 1 BLUE/GREEN, 1 MISSING.
```

## States

| State | Meaning |
|---|---|
| `ON` | Toggle is `true` or `1` in `appsettings.json` |
| `OFF` | Toggle is `false` or `0` in `appsettings.json` |
| `20%` | Percentage rollout — the raw value (e.g. `"20%"`) is shown directly |
| `BLUE` / `GREEN` | Blue-green deployment slot — shown in uppercase |
| `MISSING` | Toggle key is used in code but has no entry in any `appsettings*.json` file |

## Multi-environment support

### All environments (default)

When no `--env` flag is given, FtrIO.OneTwo finds every `appsettings*.json` in the project tree and renders a separate table for each one. The environment name is derived from the filename — `appsettings.Staging.json` becomes `Staging`, and the base `appsettings.json` is shown verbatim. Each table header includes the full path to the file it was read from so there is never any ambiguity about which config is being shown.

Duplicate environment names are deduplicated — if the same name appears in both the source directory and `bin/`, the first one found wins.

### Targeting a specific environment

Use `--env` to read a single environment. FtrIO.OneTwo applies FtrIO's overlay model: the environment-specific file's values win, and the base `appsettings.json` fills any gaps. The full path to the overlay file is shown in the table header.

```bash
ftrio-onetwo --source C:\Projects\MyApp --env Staging
ftrio-onetwo --source C:\Projects\MyApp --env Production
```

```json
// appsettings.json — base config
{
  "Toggles": {
    "SendWelcomeEmail": true,
    "NewCheckoutFlow": false,
    "PaymentV2": "blue"
  }
}

// appsettings.Staging.json — overlay (only differing values needed)
{
  "Toggles": {
    "NewCheckoutFlow": "50%"
  }
}
```

With this setup, `--env Staging` resolves `NewCheckoutFlow` to `50%` and fills `SendWelcomeEmail` and `PaymentV2` from the base.

> **Note:** FtrIO deliberately ignores `ASPNETCORE_ENVIRONMENT`, and so does this tool. Use `--env` on the command line to target a specific environment.

## Building from source

```bash
cd FtrIO.OneTwo
dotnet build
dotnet run -- --source <path>
dotnet run -- --source <source-path> --config <config-path>
```

## Related

- [FtrIO](https://github.com/FtrOnOff/FtrIO) — the feature toggle library this tool supplements
