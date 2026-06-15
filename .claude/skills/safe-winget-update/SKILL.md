---
name: safe-winget-update
description: Safe, reviewed winget update flow for this machine. Scans for available winget upgrades, scores each for safety (version age, jump size, category, publisher), researches known issues for the exact target versions, and produces a dated HTML report plus a curated, self-elevating apply-updates.cmd that pins exact versions. Does NOT install anything automatically. Use when the user says "check for updates", "safe winget update", "what can I update", "run the winget agent", or wants a reviewed update report before applying.
---

# Safe winget update flow

You orchestrate a deterministic C# engine plus your own judgment to produce a *reviewed* update plan. You never install anything — the user runs the generated `.cmd` themselves.

All commands assume the current directory is the repo root (the engine lives at `src/WingetAgent`).

## Step 0: Preflight (especially on a fresh clone)

Check prerequisites before scanning. Run the checks, and only stop if something is actually missing.

1. **winget** — run `winget --version`. If it's missing, the machine needs the App Installer (`https://aka.ms/getwinget`); stop and tell the user. (This tool is Windows-only.)
2. **.NET 10 SDK** — run `dotnet --version`. If it's absent or below `10`, tell the user to install the .NET 10 SDK (`https://dotnet.microsoft.com/download/dotnet/10.0`, or `winget install Microsoft.DotNet.SDK.10`) and stop. Do not try to install it silently.
3. **Build** — the first `dotnet run` restores NuGet packages and compiles; this can take a minute. No separate build step is needed.
4. **GitHub token (optional)** — run `dotnet run --project src/WingetAgent -c Release -- token`. If it reports "not configured", enrichment still works but is capped at ~25 packages/run. Mention this and offer to help set up a least-privilege token (`wingetagent token --set ...`), but **proceed either way** — a missing token is not blocking.

## Step 1: Scan

```
dotnet run --project src/WingetAgent -c Release -- scan
```

- It prints a summary table and a final line `Run directory: <abs path>`. **Capture that path** — call it `RUNDIR`. Runs live under `~/.winget-agent/runs/<timestamp>/`, outside the repo.
- If it reports "System is up to date", report that and stop.

## Step 2: Analyze

Read `RUNDIR/updates.json`. Each entry has the winget fields, derived `category`/`jump`/`publisher`, enrichment (`releaseDate`, `ageDays`, `recentVersions`), a run-over-run `baseline` (`New`/`Updated`/`Pending` + `pendingDays`), and a `score` with a `factors` breakdown. The run also lists `resolvedSinceBaseline` (applied since last run) and `baselineDate`.

Use the baseline to focus your research: **New** and **Updated** items changed since last run and deserve a closer look; **Pending** items have been available a while (likely lower risk).

For each update that is **Risky, a Major jump, a Driver/System component, very fresh (`ageDays` < 7), unknown age, or otherwise non-obvious**, use `WebSearch` to look up known problems with the **exact target version** — package name + target version + terms like "issues", "regression", "breaking", "release notes". Confirm with `WebFetch` on the vendor/GitHub releases page when it matters.

You may skip research for obvious low-risk cases (small patch bumps from trusted publishers like Chrome/Edge) — annotate them briefly or leave them to the baseline.

Decide a `recommendation` per package you reviewed:
- **Approve** — safe to install now (also un-skips an item the baseline marked Risky).
- **Review** — install with awareness; note the caveat.
- **Skip** — do not install now; this comments the line out in the `.cmd` regardless of band.

## Step 3: Write annotations

Write `RUNDIR/annotations.json` — an array of objects (only for packages you actually have something to say about):

```json
[
  {
    "id": "Exact.PackageId",
    "adjustedScore": 40,
    "recommendation": "Skip",
    "notes": "One or two sentences: why, and what to wait for.",
    "sources": ["https://..."]
  }
]
```

`adjustedScore` and `sources` are optional. `id` must match the package `id` from `updates.json` exactly. Source links must be `http(s)` (others are rendered as inert text).

## Step 4: Build the final report

```
dotnet run --project src/WingetAgent -c Release -- build-report -i "RUNDIR"
```

Regenerates `report.html` and `apply-updates.cmd` in `RUNDIR`, merging your annotations.

## Step 5: Summarize

Tell the user:
- Counts by band (Safe / Review / Risky) and how many you flagged Skip.
- What changed since the last run (new / updated / resolved), if there was a baseline.
- The riskiest / most notable items and *why*, citing what you found.
- How to view it: `dotnet run --project src/WingetAgent -c Release -- open` (report in browser) or `... open --folder` (the run folder). Mention the `RUNDIR` path too.
- That they should review, optionally curate the `.cmd` (toggle `REM ` on/off), then **run `apply-updates.cmd` as administrator** to apply. It self-elevates, labels each install `[n/N]`, prints `OK`/`** FAILED` per package, and ends with a failure-count summary — so any package winget couldn't install is named.

**Never run `apply-updates.cmd` yourself.** Applying updates is the user's explicit action.

## Notes
- Runs accumulate under `~/.winget-agent/runs/` as the local update history — don't delete old ones.
- The engine is fully usable without you (`scan` alone writes a baseline report + cmd); your value is the researched annotations and the summary.
