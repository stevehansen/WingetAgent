# WingetAgent — STRIDE Threat Model

## System Overview

WingetAgent is a local Windows tool that produces a *reviewed* winget update plan. A deterministic C# **Engine** enumerates available **Updates** (via the winget CLI), enriches them from the GitHub `microsoft/winget-pkgs` repository, and assigns a **Safety Score**. A Claude **Skill** researches the risky ones and writes **Annotations**. The Engine then renders a **Report** (`report.html`) and an **Apply Script** (`apply-updates.cmd`). The user reviews the Report, optionally curates the Apply Script, and runs it — at which point it **self-elevates to administrator** and installs the pinned versions.

> Terms in **bold** are defined in `UBIQUITOUS_LANGUAGE.md`.

### Components & users
- **User**: a single machine owner/operator running the tool interactively. No multi-tenancy, no network service.
- **Engine** (`src/WingetAgent`, .NET 10): `scan`, `build-report`, `token`, and `open` commands.
- **Skill** (Claude Code): orchestration + web research.
- **winget CLI** + configured **winget sources** (default: `winget`, `msstore`).
- **GitHub API / raw.githubusercontent.com**: read-only enrichment.

### Data flow

```
  winget sources ─────►┌──────────────────────────────────────────┐
  (Internet)           │  winget CLI ──► Engine (scan)             │
                       │                  parse → classify → score │
  GitHub winget-pkgs ─►│  HTTPS  ───────► enrich (ReleaseDate)     │
  (Internet, API)      └───────────────┬──────────────────────────┘
        ▲                              │ writes
        │ GITHUB_TOKEN (optional)      ▼
        │                     runs/<ts>/updates.json
                                       │
                  Skill: read → web-search → write annotations.json
                                       │
                              Engine (build-report)
                                       │ writes
                                       ▼
                   runs/<ts>/report.html      apply-updates.cmd
                          (human review)             │ user runs
                                                      ▼  (UAC: elevate)
                                       winget install --version <pinned>  [ADMIN]
                                                      ▼
                                              installed software
```

### Trust boundaries
1. **Internet → Engine**: winget source responses and GitHub API responses are untrusted input.
2. **Local filesystem → Admin execution**: `runs/<ts>/` artifacts cross into an administrative context the moment the user runs the Apply Script. This is the primary boundary.
3. **Skill → Engine**: `annotations.json` is data the Engine consumes when rendering.

### Data classification
| Data | Classification | Notes |
| ---- | -------------- | ----- |
| `GITHUB_TOKEN` | **Credential / sensitive** | Optional; lifts GitHub rate limit. |
| `updates.json` (installed software + versions) | Internal / recon-sensitive | Reveals the machine's software inventory and exact versions. |
| `report.html`, `apply-updates.cmd` | Internal | The admin action plan. |
| Run history (`runs/`) | Internal / audit | No PII or financial data anywhere. |

---

## STRIDE Analysis

Scoring: **Likelihood (1–4) × Impact (1–4) = Score**. High priority = score ≥ 8.

### Spoofing

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| S1 | Falsified enrichment data makes a bad Update look **Safe** | Compromised winget-pkgs manifest or winget source returns a misleading `ReleaseDate`/version, inflating the Safety Score | 1 | 3 | 3 | Score is advisory, not authoritative; human reviews the Report; exact-version pinning; HTTPS to GitHub |
| S2 | MITM of GitHub API/raw responses | Network attacker spoofs `api.github.com` / `raw.githubusercontent.com` | 1 | 2 | 2 | HTTPS with OS certificate validation (HttpClient default) |

**Countermeasures:** All enrichment is over HTTPS. The Safety Score is explicitly advisory — the human reviewer and exact-version pinning are the real gates, not the score.

### Tampering

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| **T1** | **Command injection into the Apply Script** | A package **Id**/version from a malicious or custom winget source (or a poisoned manifest) contains shell metacharacters that break out of the `winget install` line → arbitrary commands run at **admin** | 2 | 4 | **8** | **[v1]** Id and version are quoted *and* validated against a strict charset before emit; lines with unsafe characters are emitted **blocked** (commented, never executed); the progress banners echo id/version unquoted but escape batch metacharacters (`& < > \| ^ ( )`) via `EchoSafe` |
| **T2** | **Pre-execution tampering of run artifacts** | Malware already on the host, or a `runs/` folder in a cloud-synced/shared-writable location, edits `apply-updates.cmd` / `updates.json` / `annotations.json` before the user runs the script → admin code execution | 2 | 4 | **8** | Human reviews the rendered Report and the `.cmd` before running; keep `runs/` in an ACL-restricted, non-synced path; exact-version pinning prevents silent post-approval version swap |
| T3 | Misleading Report tricks the reviewer | Tampered `report.html` hides a malicious Update or shows a false score | 2 | 3 | 6 | All rendered fields are HTML-encoded; the `.cmd` is plain-text and independently reviewable; both live side-by-side in the same Run |

**Countermeasures:** Strict input validation + quoting of every emitted `--id`/`--version`; conservative defaults (Risky/Skip lines commented out); the human-readable Report and the plain-text Apply Script are cross-checkable.

### Repudiation

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| R1 | Run history can be altered/deleted | No integrity protection on the run history; entries can be edited | 2 | 1 | 2 | Single-user tool; history is dated, self-contained folders under the user profile (`~/.winget-agent/runs/`) protected by normal user-profile ACLs; operators wanting a stronger audit trail can archive runs to a controlled store |

**Countermeasures:** Each Run is a dated, self-contained folder under the per-user profile (outside the repo). For a single-operator tool this is sufficient; a stronger trail is an operator choice (archive/copy elsewhere).

### Information Disclosure

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| **I1** | **GITHUB_TOKEN exposure / over-privilege** | Token read from the *global environment* is visible to every child process, shell, crash dump, and screen-share; users may reuse a broad-scoped token | 2 | 3 | 6 | **[v1]** Token resolved from per-user **.NET user-secrets** first (outside the repo and the global env), then `GITHUB_TOKEN`; never logged or written to artifacts; `wingetagent token` guides creating a **least-privilege** (public read / no-scope) token and storing it in user-secrets |
| I2 | Stored XSS / malicious link in the Report | A package name/note, or an Annotation `sources` URL with a `javascript:` scheme, executes when the Report is opened | 2 | 2 | 4 | **[v1]** All text is HTML-encoded; Annotation links are restricted to `http(s)` schemes |
| I3 | Software-inventory disclosure | `updates.json` (and the Report) reveal the exact installed software and versions — recon value if leaked | 2 | 2 | 4 | Runs are written under the user profile (`~/.winget-agent/runs/`), never into the repo; the shared/MIT repository ignores `runs/`, so inventory data is not published by accident |

**Countermeasures:** Least-privilege, out-of-environment token storage; output encoding and link-scheme allowlisting; no secret ever reaches an artifact or log.

### Denial of Service

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| D1 | GitHub rate-limit exhaustion | Many packages × 2 calls exceeds 60/hr unauthenticated, stalling enrichment | 3 | 1 | 3 | Enrichment is capped (25 unauth) and **degrades gracefully** to "age unknown"; rate-limit 403 latches and short-circuits; `GITHUB_TOKEN` raises the ceiling to 5000/hr |
| D2 | Malformed winget output | Unexpected table shape breaks the parser | 1 | 1 | 1 | Parser anchors on header column offsets and stops at the first blank/short line; failure yields zero Updates, not a crash |

**Countermeasures:** Bounded work, graceful degradation, no unbounded retries.

### Elevation of Privilege

| ID | Threat | Attack path | L | I | Score | Mitigation |
| -- | ------ | ----------- | - | - | ----- | ---------- |
| **E1** | **Self-elevating Apply Script executes unverified content as admin** | The `.cmd` runs `Start-Process -Verb RunAs` and installs without verifying its own integrity — so any successful T1/T2 becomes administrative code execution | 2 | 4 | **8** | Mitigated *upstream* by T1 (validation) and T2 (review + safe storage + pinning); the script only ever contains validated, pinned `winget install` lines; Risky lines are inert by default |
| E2 | Vendor installers run as admin | Installing any upgrade runs the package's own installer with admin rights (inherent to winget) | 2 | 3 | 6 | **Accepted risk** — inherits winget/source trust; exact-version pinning prevents post-approval substitution; user explicitly initiates |

**Countermeasures:** The Engine never installs; only the user runs the Apply Script. The script's content is constrained to validated, pinned install commands, and the high-risk default is *inaction* (commented-out lines).

---

## Risk Summary

### High-priority threats (score ≥ 8)
- **T1 — Command injection into the Apply Script** → mitigated in v1 by strict Id/version validation + quoting; unsafe entries are blocked, not executed.
- **T2 — Pre-execution tampering of run artifacts** → mitigated by mandatory human review, exact-version pinning, and guidance to keep `runs/` in an ACL-restricted, non-synced location. *Partly operational — see residual risks.*
- **E1 — Self-elevation executes unverified content** → the unifying EoP; neutralized upstream by T1/T2. The script contains only validated, pinned commands.

### Residual risks (accepted / operational)
- **E2** — Upgrades inherently run vendor installers as admin. Accepted; bounded by exact-version pinning and explicit user initiation.
- **T2 (operational portion)** — WingetAgent cannot fully prevent host-resident malware from editing artifacts before execution. Compensating controls are review + pinning + safe storage; a future hardening could sign/hash the `.cmd` and verify on launch.
- **Supply chain** — WingetAgent installs whatever the configured winget sources report as the upgrade; it inherits winget's source-trust model. Pinning exact versions limits "latest"-swap attacks.

---

## Security Controls Summary

| Control category | Implementation |
| ---------------- | -------------- |
| Input validation | Package Id/version validated against a strict charset and quoted before emission; unsafe entries blocked; progress banners escape batch metacharacters (`EchoSafe`) |
| Output encoding | All Report fields HTML-encoded; Annotation links restricted to `http(s)` |
| Secret management | Token from .NET user-secrets (preferred) or `GITHUB_TOKEN`; never logged or persisted to artifacts; `wingetagent token` guides least-privilege setup |
| Least privilege | Recommended token has no scopes (public read only); Engine performs no privileged action |
| Safe defaults | Risky / Skip Updates commented out in the Apply Script; tool never auto-installs |
| Integrity | Exact-version pinning; dated Run history under the user profile |
| Transport security | HTTPS with OS cert validation for all GitHub access |
| Graceful degradation | Rate-limit and parse failures degrade rather than crash |

---

## Review History

| Version | Date | Author | Changes |
| ------- | ---- | ------ | ------- |
| v1 | 2026-06-15 | Initial model (Claude Code) | First STRIDE analysis for WingetAgent v1; T1/I1/I2 mitigations implemented in code |

Next review: on any change to artifact generation, token handling, the winget integration, or annually (2027-06-15), whichever comes first.

---

## References
- [STRIDE threat model (Microsoft)](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats)
- [winget security & sources](https://learn.microsoft.com/en-us/windows/package-manager/)
- [GitHub fine-grained personal access tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [OWASP Command Injection](https://owasp.org/www-community/attacks/Command_Injection)
- `UBIQUITOUS_LANGUAGE.md` — domain glossary
