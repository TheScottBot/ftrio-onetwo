![FtrIO.onetwo](onetwo.png)

A .NET CLI audit and migration tool for [FtrIO](https://github.com/FtrOnOff/FtrIO) feature toggles.

---

## Quickstart

```bash
dotnet tool install -g FtrIO.onetwo
ftrio.onetwo --source C:\Projects\MyApp
```

That's it. FtrIO.onetwo scans every `.cs` file in the directory, finds every `[Toggle]` reference, and cross-references it against every `appsettings*.json` it can find — giving you an instant table of what's ON, OFF, or MISSING across all your environments.

```
── Staging  C:\Projects\MyApp\appsettings.Staging.json
╭──────────────────┬──────────────────┬──────────┬─────────┬───────────────────┬──────╮
│ Toggle Key       │ Method           │ Source   │  State  │ File              │ Line │
├──────────────────┼──────────────────┼──────────┼─────────┼───────────────────┼──────┤
│ NewCheckoutFlow  │ NewCheckoutFlow  │ [Toggle] │   50%   │ Services\Order.cs │    9 │
│ SendWelcomeEmail │ SendWelcomeEmail │ [Toggle] │   ON    │ Services\Email.cs │   22 │
│ UnknownFeature   │ UnknownFeature   │ ManualCall│ MISSING │ Controllers\Ho...│   42 │
╰──────────────────┴──────────────────┴──────────┴─────────┴───────────────────┴──────╯
3 toggle(s). 1 ON, 0 OFF, 1 PERCENTAGE, 0 BLUE/GREEN, 1 MISSING.
```

**Requirements:** [.NET SDK](https://dotnet.microsoft.com/download) 6, 8, or 10

---

## Coming from LaunchDarkly, Flagsmith, Unleash, or Microsoft.FeatureManagement?

> **⚠️ Experimental** — `migrate` and `import` are available now but have not been tested against live LaunchDarkly, Flagsmith, Unleash, or Microsoft.FeatureManagement accounts. If you try this path we'd love to hear how it goes — please [open an issue](https://github.com/FtrOnOff/FtrIO.onetwo/issues) with your findings.

FtrIO.onetwo makes onboarding from another provider a two-step process.

### Step 1 — See what needs changing

`migrate` scans your code for existing SDK call patterns, cross-references them against your live flag state, and tells you exactly what to change and in what order.

```bash
# From LaunchDarkly
ftrio.onetwo migrate --from launchdarkly \
  --api-key sdk-xxx --project my-project --env production \
  --source C:\Projects\MyApp --markdown plan.md

# From Flagsmith
ftrio.onetwo migrate --from flagsmith \
  --api-key env-xxx \
  --source C:\Projects\MyApp --markdown plan.md

# From Unleash — self-hosted, needs base URL
ftrio.onetwo migrate --from unleash \
  --api-key my-admin-token --url https://unleash.example.com \
  --source C:\Projects\MyApp --markdown plan.md

# From Microsoft.FeatureManagement — no API key needed, reads local config
ftrio.onetwo migrate --from microsoft.featuremanagement \
  --source C:\Projects\MyApp --markdown plan.md
```

The report categorises every flag:

| Status | Meaning |
|---|---|
| ✅ Ready to migrate | Straightforward `[Toggle]` replacement — action shown |
| ⚠️ Needs review | Targeting rules or complex config — options shown |
| ❌ Cannot migrate | JSON flags — recommend `IConfiguration` options pattern |
| Stale flag | In provider but not in code — safe to delete |
| Deleted flag | In code but gone from provider — potentially broken |

**Detected patterns:**

```csharp
// LaunchDarkly
client.BoolVariation("flag-key", user, false)
client.StringVariation("flag-key", user, "default")

// Flagsmith
flagsmithClient.HasFeatureFlagAsync("flag-key")

// Unleash
unleashClient.IsEnabled("flag-key")
unleashClient.GetVariant("flag-key")   // detected as cannot migrate (multivariate)

// Microsoft.FeatureManagement
[FeatureGate("flag-key")]
featureManager.IsEnabled("flag-key")
featureManager.IsEnabledAsync("flag-key")
```

### Step 2 — Snapshot current flag state

`import` pulls the current value of every flag from your provider and writes it into the `Toggles` section of `appsettings.json` — so FtrIO is already configured with the right state before you change a single line of code.

```bash
# From LaunchDarkly
ftrio.onetwo import --source launchdarkly \
  --api-key sdk-xxx --project my-project --env production \
  --config C:\Projects\MyApp\appsettings.json

# From Flagsmith
ftrio.onetwo import --source flagsmith \
  --api-key env-xxx \
  --config C:\Projects\MyApp\appsettings.json

# From Unleash — self-hosted, needs base URL
ftrio.onetwo import --source unleash \
  --api-key my-admin-token --url https://unleash.example.com \
  --config C:\Projects\MyApp\appsettings.json

# From Microsoft.FeatureManagement — reads FeatureManagement section, writes to Toggles
ftrio.onetwo import --source microsoft.featuremanagement \
  --file C:\Projects\MyApp\appsettings.json \
  --config C:\Projects\MyApp\appsettings.json

# From flagd, env vars, or HTTP
ftrio.onetwo import --source flagd --file C:\flags\flags.json --config ...
ftrio.onetwo import --source env --prefix FEATURE_ --config ...
ftrio.onetwo import --source http --url https://config.example.com/flags --config ...
```

After importing, run `ftrio.onetwo --source C:\Projects\MyApp` to verify every flag resolved correctly before you touch any call sites.

### Full onboarding workflow

```bash
# 1. See what needs to change
ftrio.onetwo migrate --from launchdarkly --api-key sdk-xxx --project my-project --env production --source C:\Projects\MyApp --markdown plan.md

# 2. Snapshot current flag state
ftrio.onetwo import --source launchdarkly --api-key sdk-xxx --project my-project --env production --config C:\Projects\MyApp\appsettings.json

# 3. Verify the snapshot looks right
ftrio.onetwo --source C:\Projects\MyApp

# 4. Work through plan.md — migrate call sites at your own pace

# 5. Verify everything is wired up
ftrio.onetwo --source C:\Projects\MyApp
```

---

## Want to leave? Eject cleanly.

> **⚠️ Experimental** — `eject` is available now but has not been tested against live provider accounts. If you try this path we'd love to hear how it goes — please [open an issue](https://github.com/FtrOnOff/FtrIO.onetwo/issues) with your findings.

`eject` is the reverse of `migrate` — it generates a complete exit report from FtrIO back to LaunchDarkly, Flagsmith, Microsoft.FeatureManagement, or Unleash. It can optionally create your flags in the target system with their current values before you change a line of code.

```bash
ftrio.onetwo eject --to <target> [options]
```

```bash
# Report only — no API calls, just show what would change
ftrio.onetwo eject --to launchdarkly --source C:\Projects\MyApp

# Create flags in LaunchDarkly and write a report
ftrio.onetwo eject --to launchdarkly \
  --api-key sdk-xxx --project my-project --env production \
  --source C:\Projects\MyApp --markdown eject-report.md

# Flagsmith
ftrio.onetwo eject --to flagsmith \
  --api-key srv-xxx --project my-project \
  --source C:\Projects\MyApp --markdown eject-report.md

# Unleash
ftrio.onetwo eject --to unleash --api-key my-token --source C:\Projects\MyApp

# Microsoft.FeatureManagement — lowest friction, no API needed
ftrio.onetwo eject --to microsoft.featuremanagement --source C:\Projects\MyApp --markdown eject-report.md
```

The report covers every toggle key found in code: its current value, the normalised key name in the target system, what code change is needed, and a ready-to-paste config snippet.

**Key normalisation:**

| Target | Convention | Example |
|---|---|---|
| `launchdarkly` | kebab-case | `SendWelcomeEmail` → `send-welcome-email` |
| `flagsmith` | snake_case | `SendWelcomeEmail` → `send_welcome_email` |
| `microsoft.featuremanagement` | PascalCase (unchanged) | `SendWelcomeEmail` → `SendWelcomeEmail` |
| `unleash` | kebab-case | `SendWelcomeEmail` → `send-welcome-email` |

### Microsoft.FeatureManagement is the lowest-friction exit

`[FeatureGate("SendWelcomeEmail")]` is a near like-for-like replacement for `[Toggle]` — same attribute placement, same PascalCase key, no key normalisation needed. The eject report for `microsoft.featuremanagement` is a checklist of find-and-replace operations plus a config section rename from `Toggles` to `FeatureManagement`.

```bash
ftrio.onetwo eject --to microsoft.featuremanagement --source C:\Projects\MyApp --markdown eject-report.md
```

No lock-in. No trap door.

---

## Deployment safety

`export-manifest` and `release-check` work together to catch missing toggle config before it reaches production.

### How it works

**In your app's CI pipeline** (on every push):

```bash
ftrio.onetwo export-manifest --source ./src --output toggles.manifest.json
```

This writes a JSON snapshot of every toggle key the codebase references. The manifest is uploaded as a build artifact.

**In your deployment pipeline** (before deploying):

```bash
ftrio.onetwo release-check \
  --manifest toggles.manifest.json \
  --config appsettings.Production.json \
  --env-name Production \
  --markdown release-check-report.md
```

This validates every key in the manifest is present in the target config. If anything is missing the check fails, shows exactly where each missing key is used in code, and prints a ready-to-paste JSON block of suggested additions.

```
✅  SendWelcomeEmail    present   true
❌  PaymentV2           MISSING
    Used at:    Services\PaymentService.cs:88
    Suggested:  "PaymentV2": "false"

Release to Production is BLOCKED.
```

### GitHub Actions

```yaml
# App CI pipeline
- uses: FtrOnOff/export-manifest-action@v1
  with:
    source: ./src

# Deployment pipeline
- uses: FtrOnOff/release-check-action@v1
  with:
    artifact-name: toggle-manifest
    config-url: ${{ secrets.PRODUCTION_CONFIG_URL }}
    config-auth-header: ${{ secrets.PRODUCTION_CONFIG_AUTH }}
    env-name: Production
    fail-on-missing: true
```

Declare `needs: release-check` on your deploy job to block it automatically if the check fails.

---

## Reference

### `ftrio.onetwo` — audit

```
ftrio.onetwo [--source <path>] [--config <path>] [--env <name>] [--markdown <file>]
```

| Argument | Description |
|---|---|
| `--source` | Directory to scan for toggle usage in `.cs` files. Defaults to current directory. |
| `--config` | Directory to search for `appsettings*.json` files. Defaults to `--source`. |
| `--env` | Show a single environment using the base+overlay model (e.g. `--env Staging`). |
| `--markdown` | Also write the results to a markdown file. |

Both `--source` and `--config` can be passed as positional arguments.

**Toggle states:**

| State | Meaning |
|---|---|
| `ON` | `true` or `1` in `appsettings.json` |
| `OFF` | `false` or `0` in `appsettings.json` |
| `20%` | Percentage rollout — raw value shown directly |
| `BLUE` / `GREEN` | Blue-green deployment slot |
| `MISSING` | Used in code but not present in any config file |

**Detected patterns:** `[Toggle]`, `[ToggleAsync]`, `ExecuteMethodIfToggleOn`, `ExecuteMethodIfToggleOnAsync`

> FtrIO deliberately ignores `ASPNETCORE_ENVIRONMENT`. Use `--env` on the command line to target a specific environment.

---

### `ftrio.onetwo import`

| Argument | Description |
|---|---|
| `--source` | Source type: `launchdarkly`, `flagsmith`, `unleash`, `microsoft.featuremanagement`, `flagd`, `env`, `http` |
| `--api-key` | Auth key for LaunchDarkly, Flagsmith, or Unleash |
| `--project` | LaunchDarkly project key |
| `--env` | Environment name |
| `--file` | Local file for `flagd` or `microsoft.featuremanagement` source |
| `--url` | Base URL for `unleash` source, or endpoint URL for `http` source |
| `--prefix` | Prefix to strip for `env` source (e.g. `FEATURE_`) |
| `--config` | Path to `appsettings.json` to write. Defaults to `appsettings.json` in current directory. |
| `--dry-run` | Print what would change without writing |
| `--overwrite` | Replace the entire `Toggles` section (default: merge) |
| `--sync` | Run continuously, polling every `--interval` seconds |
| `--interval` | Poll interval in seconds (default: 30) |
| `--markdown` | Write a markdown summary |
| `--fail-on-warnings` | Exit code 3 if any flags were approximated |

**Exit codes:** `0` success, `1` source unreachable, `2` write failure, `3` warnings (with `--fail-on-warnings`)

---

### `ftrio.onetwo migrate`

| Argument | Description |
|---|---|
| `--from` | SDK to scan for: `launchdarkly`, `flagsmith`, `unleash`, `microsoft.featuremanagement` |
| `--source` | Directory to scan for `.cs` files |
| `--api-key` | Optional — fetches live flag state. Not needed for `microsoft.featuremanagement`. |
| `--project` | LaunchDarkly project key |
| `--env` | Environment name |
| `--url` | Unleash server base URL (e.g. `https://unleash.example.com`) |
| `--config` | Config file for `microsoft.featuremanagement` flag values |
| `--exclude` | Comma-separated flag keys to exclude |
| `--markdown` | Write the full report to a markdown file |
| `--fail-on-unsupported` | Exit code 1 if any flags cannot be migrated |

---

### `ftrio.onetwo eject`

| Argument | Description |
|---|---|
| `--to` | Target system: `launchdarkly`, `flagsmith`, `microsoft.featuremanagement`, `unleash` |
| `--source` | Directory to scan for `.cs` files. Defaults to current directory. |
| `--config` | Path to `appsettings.json`. Defaults to `appsettings.json` in `--source`. |
| `--api-key` | API key for the target. If omitted, report only — no flags created. |
| `--project` | Project key (required for LaunchDarkly and Flagsmith with `--api-key`) |
| `--env` | Environment to create flags in (required for LaunchDarkly with `--api-key`) |
| `--exclude` | Comma-separated toggle keys to exclude |
| `--markdown` | Write the full report to a markdown file |
| `--dry-run` | Show what would be created without making any API calls |

**Exit codes:** `0` all flags created cleanly, `1` flags missing or failed, `2` source/config not found, `3` API unreachable

---

### `ftrio.onetwo export-manifest`

| Argument | Description |
|---|---|
| `--source` | Directory to scan. Defaults to current directory. |
| `--output` | Output file. Defaults to `toggles.manifest.json`. |
| `--pretty` | Pretty-print JSON (default: true) |

**Exit codes:** `0` success, `1` source not found or no `.cs` files, `2` write failure

---

### `ftrio.onetwo release-check`

| Argument | Description |
|---|---|
| `--manifest` | Path to the manifest JSON. Required. |
| `--config` | Path to a local `appsettings.json` to validate against. |
| `--config-url` | URL to fetch the target config from. Mutually exclusive with `--config`. |
| `--env-name` | Display name for the environment in the report. |
| `--markdown` | Write the full report to a markdown file. |
| `--fail-on-missing` | Exit code 1 if any keys are missing (default: true). |
| `--warn-only` | Always exit 0 but emit warnings for missing keys. |

**Exit codes:** `0` all present, `1` keys missing, `2` manifest not found/invalid, `3` config unreachable

---

## The FtrIO ecosystem

- [**FtrIO**](https://github.com/FtrOnOff/FtrIO) — the core library. Weaves `[Toggle]` into your IL at compile time, reads state from `appsettings.json` at runtime.
- [**FtrIO.Toaster**](https://github.com/FtrOnOff/FtrIO.Toaster) — a lightweight web UI for managing toggles live without editing files or restarting.
- [**FtrIO.onetwo**](https://github.com/FtrOnOff/FtrIO.onetwo) — this tool.

## Building from source

```bash
cd FtrIO.onetwo
dotnet build
dotnet run -- --source <path>
```
