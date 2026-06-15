# WingetAgent

A **safe, reviewed winget update flow**. It never installs anything automatically. Instead it:

1. **Scans** the machine for available winget upgrades.
2. **Enriches** each one with version history and release date from the [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) manifests.
3. **Scores** every update for safety (0–100) from how mature the target version is, how big the version jump is, what kind of software it is, and whether the publisher is known.
4. **Reviews** them (via the Claude skill) — researching known issues for the exact target versions and annotating each.
5. Produces a dated **HTML report** and a curated, self-elevating **`apply-updates.cmd`** that pins every package to an exact version. You review, then run the `.cmd` as administrator.

Each run is kept under `~/.winget-agent/runs/<timestamp>/` (outside the repo), so the `.cmd` files double as a local update history.

## How it works

```
skill (safe-winget-update)            C# engine (src/WingetAgent)
─ orchestrates + judges               ─ deterministic, no Claude needed
  scan ─────────────────────────────► winget upgrade → enrich → score
  read updates.json                     writes updates.json (+ baseline html/cmd)
  web-search known issues
  write annotations.json
  build-report ─────────────────────► merge annotations → report.html + apply-updates.cmd
  summarize to user
```

The engine and the agent meet only through JSON files (`updates.json`, `annotations.json`), so each stays simple and independently runnable.

## Requirements

- **Windows** with [winget](https://aka.ms/getwinget) (App Installer)
- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/10.0) or `winget install Microsoft.DotNet.SDK.10`)
- *(optional)* a GitHub token for full enrichment — see [GitHub token](#github-token)

## Quick start

### Via the skill (recommended)

Clone the repo, open Claude Code in the folder, and run:

```
/safe-winget-update
```

On a fresh clone the skill runs a preflight (checks winget + the .NET 10 SDK, notes whether a token is set), then scans, researches the risky updates, writes annotations, rebuilds the report, and summarizes — pointing you at the report and the `.cmd`. The first run restores packages and compiles, so it takes a minute. Nothing is installed without your explicit action.

### Engine directly (deterministic only, no review)

```powershell
# Scan, enrich, score → ~/.winget-agent/runs/<timestamp>/{updates.json, report.html, apply-updates.cmd}
dotnet run --project src/WingetAgent -c Release -- scan

# Open the latest report in your browser (or the folder in Explorer)
dotnet run --project src/WingetAgent -c Release -- open
dotnet run --project src/WingetAgent -c Release -- open --folder

# Re-render after editing annotations.json (or to apply Claude's review)
dotnet run --project src/WingetAgent -c Release -- build-report -i <rundir>
```

Useful flags on `scan`:

| Flag | Effect |
|------|--------|
| `-o, --output <DIR>` | Output run directory (default `~/.winget-agent/runs/<timestamp>`) |
| `--no-enrich` | Skip GitHub enrichment (no release dates / history) |
| `--max-enrich <N>` | Cap how many packages to enrich (0 = auto) |
| `--no-include-unknown` | Exclude packages with an undeterminable installed version |

## Applying updates

Open `report.html`, optionally curate `apply-updates.cmd` (each install line can be toggled by adding/removing a leading `REM `), then **run `apply-updates.cmd` as administrator**. It self-elevates via UAC on launch. Risky items and anything the review flagged `Skip` are commented out by default. Every package is pinned to an exact version, and any id/version containing unsafe characters is `BLOCKED` rather than run.

## Safety scoring

Starts at 70, adjusted by independent factors (each shown in the report):

- **Age of target version** — very fresh releases are penalized; mature ones rewarded.
- **Version jump** — patch `+10`, minor `0`, major `-20`, unknown `-8`.
- **Category** — driver `-20`, system/runtime `-15`, developer `-5`, browser `+5`, app `0`.
- **Publisher** — trusted `+10`, unrecognized `-5`.
- **Unknown installed version** — `-10`.

Bands: **Safe** ≥ 75, **Review** ≥ 50, **Risky** < 50.

## GitHub token

Enrichment uses the GitHub API (~2 calls/package). Unauthenticated callers get 60 requests/hour, so without a token only ~25 packages are enriched per run (the engine warns and degrades gracefully). A token lifts this to 5000/hr.

A **least-privilege** token is enough — a classic token with **no scopes** is still authenticated for public reads and can touch nothing else. Set one up:

```powershell
dotnet run --project src/WingetAgent -c Release -- token            # shows status + the exact creation URL
dotnet run --project src/WingetAgent -c Release -- token --set ghp_xxx   # stores it in per-user .NET user-secrets
```

The token is stored in your user profile via .NET user-secrets — **not** in the repo or the global environment — and is never logged or written to any run artifact. (Setting the `GITHUB_TOKEN` environment variable also works, but is less private.)

## Where things live

| Path | What |
|------|------|
| `~/.winget-agent/runs/<timestamp>/` | Per-run output + local history (outside the repo) |
| user-secrets store | Your GitHub token (per-user) |

## Security

See [`STRIDE.md`](STRIDE.md) for the threat model. Highlights: emitted install commands are quoted and validated against command injection, the report HTML-encodes all content and only links `http(s)` sources, and the tool never installs anything itself. [`UBIQUITOUS_LANGUAGE.md`](UBIQUITOUS_LANGUAGE.md) defines the domain vocabulary.

## License

[MIT](LICENSE).
