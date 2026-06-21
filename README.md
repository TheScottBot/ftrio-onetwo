![FtrIO.onetwo](onetwo.png)

A .NET CLI tool that scans a project directory for [FtrIO](https://github.com/FtrOnOff/FtrIO) feature toggle usage and reports the current state of every toggle.

Because FtrIO always resolves toggle state from `appsettings.json` at runtime, FtrIO.onetwo gives you an instant at-a-glance view of exactly what is enabled or disabled in your codebase right now — and precisely where each toggle is used — without having to open a single source file or config manually.

## What it does

FtrIO.onetwo walks a project's source tree, finds every toggle reference, cross-references it against `appsettings.json`, and outputs a table showing the current state of each toggle.

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
- [**FtrIO.onetwo**](https://github.com/FtrOnOff/FtrIO.onetwo) — a .NET CLI audit tool. Scans your source tree for every toggle reference, cross-references against `appsettings.json`, and reports each toggle's state (`ON` / `OFF` / `AB-TEST` / `TARGETED` / `RULE-BASED` / `MISSING`) with file and line number.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) 6, 8, or 10 (net6.0, net8.0, and net10.0 are all supported)

## Installation

Install as a global dotnet tool from NuGet:

```bash
dotnet tool install -g FtrIO.onetwo
```

## Usage

```
ftrio.onetwo [--source <path>] [--config <path>] [--env <name>] [--markdown <output.md>]
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
ftrio.onetwo --source C:\Projects\MyApp

# Source code and config files in separate locations
ftrio.onetwo --source C:\Projects\MyApp --config C:\Projects\MyApp\bin\Debug\net10.0

# Positional shorthand (source then config)
ftrio.onetwo "C:\Projects\MyApp" "C:\Server\configs"

# Explicitly scan against the Staging overlay
ftrio.onetwo --source C:\Projects\MyApp --env Staging

# Also emit a markdown report
ftrio.onetwo --source C:\Projects\MyApp --config C:\Server\configs --env Production --markdown toggles.md

# Scan the current directory
ftrio.onetwo
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

| State | Meaning | Resolvable at audit time? |
|---|---|---|
| `ON` | `true` or `1` in `appsettings.json` | ✅ yes |
| `OFF` | `false` or `0` in `appsettings.json` | ✅ yes |
| `ON (BLUE)` / `OFF (BLUE)` | Blue-green slot resolved via `FtrIO:BlueGreen:CurrentSlot` in config | ✅ yes |
| `BLUE` / `GREEN` | Blue-green slot — `CurrentSlot` absent, shown raw | ⚠️ needs config |
| `50%` | Plain percentage rollout | ⚠️ partial |
| `AB-TEST(50%)` | A/B experiment rollout (`ab:50`) — user bucket not resolvable at audit time | ❌ runtime |
| `AB-TEST(50% salt=round2)` | A/B experiment with an explicit salt for independent bucketing (`ab:50:round2`) | ❌ runtime |
| `TARGETED(alice,bob)` | Per-user targeting (`users:alice,bob`) — not resolvable without a user context | ❌ runtime |
| `RULE-BASED(plan equals premium)` | Attribute rule (`attribute:plan equals premium`) — not resolvable without request context | ❌ runtime |
| `MISSING` | Toggle key is used in code but has no entry in any `appsettings*.json` file | ✅ yes |

`AB-TEST`, `TARGETED`, and `RULE-BASED` all mean the config value is present and valid — the toggle is not missing. Whether it fires depends on runtime context that the audit tool cannot see statically.

### Blue-green resolution

When `FtrIO:BlueGreen:CurrentSlot` is present in `appsettings.json`, blue-green toggles resolve to `ON` or `OFF` automatically. The active slot is shown in the table header:

```
── Staging  appsettings.Staging.json  (current slot: blue)
```

If `CurrentSlot` is absent, the raw slot name (`BLUE` / `GREEN`) is shown unchanged.

### Per-user overrides

FtrIO v1.1.2 supports a `TogglesOverrides` section that pins toggle state for specific users unconditionally:

```json
"TogglesOverrides": {
  "NewDashboard": { "alice": false, "bob": true }
}
```

By default, onetwo notes which toggles have overrides at the bottom of each table:

```
⚡ TogglesOverrides present for: NewDashboard. Use --show-overrides to display per-user values.
```

Pass `--show-overrides` to add an Overrides column to the table showing the exact per-user values.

## Multi-environment support

### All environments (default)

When no `--env` flag is given, FtrIO.onetwo finds every `appsettings*.json` in the project tree and renders a separate table for each one. The environment name is derived from the filename — `appsettings.Staging.json` becomes `Staging`, and the base `appsettings.json` is shown verbatim. Each table header includes the full path to the file it was read from so there is never any ambiguity about which config is being shown.

Duplicate environment names are deduplicated — if the same name appears in both the source directory and `bin/`, the first one found wins.

### Targeting a specific environment

Use `--env` to read a single environment. FtrIO.onetwo applies FtrIO's overlay model: the environment-specific file's values win, and the base `appsettings.json` fills any gaps. The full path to the overlay file is shown in the table header.

```bash
ftrio.onetwo --source C:\Projects\MyApp --env Staging
ftrio.onetwo --source C:\Projects\MyApp --env Production
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

## Deployment safety

FtrIO.onetwo provides a two-command deployment gate that ensures your production config always has an entry for every toggle your code uses — before you deploy, not after.

### `ftrio.onetwo export-manifest`

Scans your source tree and writes a JSON manifest of every toggle key the codebase references, with its source type, file path, and line number. Run this in your app's CI pipeline on every push.

```bash
ftrio.onetwo export-manifest --source ./src --output toggles.manifest.json
```

```json
{
  "generatedAt": "2026-06-21T16:00:00Z",
  "toggles": [
    { "key": "SendWelcomeEmail", "source": "[Toggle]", "file": "Services/EmailService.cs", "line": 17 },
    { "key": "PaymentV2", "source": "[ToggleAsync]", "file": "Services/PaymentService.cs", "line": 88 }
  ]
}
```

| Argument | Description |
|---|---|
| `--source <path>` | Directory to scan for `.cs` files. Defaults to current directory. |
| `--output <file>` | Path to write the manifest. Defaults to `toggles.manifest.json`. |
| `--pretty` | Pretty-print the JSON output (default: true). |

**Exit codes:** `0` success, `1` source not found or no `.cs` files, `2` write failure

---

### `ftrio.onetwo release-check`

Reads a manifest and validates every key is present in a target `appsettings.json` — either a local file or a remote URL. Blocks the release if anything is missing.

```bash
ftrio.onetwo release-check \
  --manifest toggles.manifest.json \
  --config appsettings.Production.json \
  --env-name Production \
  --markdown release-check-report.md
```

```
FtrIO release check: Production
Manifest:  toggles.manifest.json (2 toggles)
Config:    appsettings.Production.json

✅  SendWelcomeEmail    present   true
❌  PaymentV2           MISSING
    Used at:    Services\PaymentService.cs:88
    Risk:       Toggle key not in config — will be treated as OFF at runtime
    Suggested:  "PaymentV2": "false"

── Add to appsettings.json ──────────────────────────
{
  "Toggles": {
    "PaymentV2": "false"
  }
}

── Summary ──────────────────────────────────────────
1 present ✅   1 missing ❌
Release to Production is BLOCKED.
```

| Argument | Description |
|---|---|
| `--manifest <file>` | Path to the manifest JSON. Required. |
| `--config <file>` | Path to a local `appsettings.json` to validate against. |
| `--config-url <url>` | URL to fetch the target config from. Mutually exclusive with `--config`. |
| `--env-name <name>` | Display name for the environment in the report. Defaults to the config filename. |
| `--markdown <file>` | Write the full report to a markdown file. |
| `--fail-on-missing` | Exit code 1 if any keys are missing (default: true). |
| `--warn-only` | Always exit 0 but emit warnings for missing keys. |

**Exit codes:** `0` all present, `1` keys missing, `2` manifest not found/invalid, `3` config unreachable

---

### GitHub Actions

Two companion actions are available to wire this into your pipelines.

**Step 1 — in your app's CI pipeline:**

```yaml
- uses: FtrOnOff/export-manifest-action@v1
  with:
    source: ./src
    output: toggles.manifest.json
```

The manifest is uploaded as a build artifact and retained for 30 days.

**Step 2 — in your deployment pipeline:**

```yaml
- uses: FtrOnOff/release-check-action@v1
  with:
    artifact-name: toggle-manifest
    config-url: ${{ secrets.PRODUCTION_CONFIG_URL }}
    config-auth-header: ${{ secrets.PRODUCTION_CONFIG_AUTH }}
    env-name: Production
    fail-on-missing: true
```

Missing keys emit warning annotations in the Actions UI and a single error summary when the check fails. The `deploy` job should declare `needs: release-check` so it is blocked automatically if the check fails.

**Full deployment pipeline:**

```yaml
name: Deploy to Production
on:
  release:
    types: [published]

jobs:
  release-check:
    runs-on: ubuntu-latest
    steps:
      - uses: FtrOnOff/release-check-action@v1
        with:
          artifact-name: toggle-manifest
          config-url: ${{ secrets.PRODUCTION_CONFIG_URL }}
          config-auth-header: ${{ secrets.PRODUCTION_CONFIG_AUTH }}
          env-name: Production
          fail-on-missing: true
          markdown: release-check-report.md

  deploy:
    needs: release-check
    runs-on: ubuntu-latest
    steps:
      - name: Deploy
        run: echo "deploying..."
```

Combined with the Roslyn-based toggle scanner (audit-time) and FtrIO itself (runtime), this catches missing toggle config at every stage of the pipeline.

---

## Building from source

```bash
cd FtrIO.onetwo
dotnet build
dotnet run -- --source <path>
dotnet run -- --source <source-path> --config <config-path>
```

## Related

- [FtrIO](https://github.com/FtrOnOff/FtrIO) — the feature toggle library this tool supplements
