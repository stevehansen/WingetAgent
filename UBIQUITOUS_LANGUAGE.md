# Ubiquitous Language

The shared vocabulary of WingetAgent. Use these terms in code, comments, reports, and UI. Where a term is overloaded, the canonical meaning is below and the alternatives are listed as aliases to avoid.

## Workflow & artifacts

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Scan** | One execution that enumerates, enriches, and scores every available update on a machine. | check, run (as a verb) |
| **Run** | The complete result of one Scan, persisted as a dated folder; the unit of update history. | session, batch |
| **Run Directory** | The `runs/<timestamp>/` folder holding a Run's `updates.json`, Report, and Apply Script. | RUNDIR, output dir |
| **Enrichment** | Augmenting an Update with its release date and version history from the winget-pkgs Manifest. | lookup, hydration |
| **Report** | The single self-contained `report.html` for a Run. | dashboard, page |
| **Apply Script** | The dated, self-elevating `apply-updates.cmd` that installs the curated Updates at pinned versions. | install script, batch file, the cmd |
| **Approval** | The act of curating the Apply Script (toggling install lines) before running it; the file itself is the approval record. | sign-off, confirmation |

## Update assessment

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Update** | A single available package version change that WingetAgent evaluates. | upgrade, update candidate, package |
| **Safety Score** | The engine's deterministic 0–100 rating of an Update (higher = safer). | score, risk score |
| **Score Factor** | One named contribution to a Safety Score, with its point delta and reason. | rule, criterion |
| **Band** | The Safety Score bucket: **Safe** (≥75), **Review** (≥50), **Risky** (<50). | level, tier, severity |
| **Category** | The kind of software, used in scoring: System, Driver, Developer, Browser, Application. | type, kind |
| **Version Jump** | The magnitude of the version change: Patch, Minor, Major (or Unknown). | delta, bump |
| **Publisher** | The party that ships the package (the first segment of the package Id). | vendor, author |
| **Trusted Publisher** | A Publisher on the known-good list, earning a positive Score Factor. | verified, whitelisted |
| **Release Date** | When the target version was published, read from the Manifest; drives **Age** (days since). | date |
| **Manifest** | The winget-pkgs `*.installer.yaml` describing a package version; the source of Release Date and version history. | yaml, package definition |

## Agentic review

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Annotation** | The Skill's per-Update judgment attached to a Run via `annotations.json`. | comment, override |
| **Recommendation** | An Annotation's verdict — **Approve**, **Review**, or **Skip** — which decides whether the Apply Script line is active. | advice, status |
| **Adjusted Score** | An Annotation's optional replacement for the Safety Score, shown in the Report and Apply Script. | new score, override score |

## Actors

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Engine** | The deterministic C# app (`src/WingetAgent`) that performs a Scan and renders artifacts; needs no Claude. | app, tool, CLI |
| **Skill** | The agentic layer (`safe-winget-update`) that runs a Scan, researches Updates, writes Annotations, and rebuilds the Report. | agent, flow |

## Relationships

- A **Scan** produces exactly one **Run**, stored in one **Run Directory**.
- A **Run** contains many **Updates**; each **Update** has one **Safety Score** composed of **Score Factors** and summarized by one **Band**.
- The **Skill** may attach at most one **Annotation** to an **Update**; an **Annotation** carries one **Recommendation** and an optional **Adjusted Score**.
- A **Run** yields one **Report** and one **Apply Script**; the Apply Script has one pinned install line per **Update**, with **Risky** or **Skip** lines commented out.
- The **Engine** can produce a Run without the **Skill**; the Skill never installs — only the user runs the **Apply Script**.

## Example dialogue

> **Dev:** "When the **Skill** sets a **Recommendation** of `Review`, does that change the **Band**?"
> **Domain expert:** "No. The **Band** is derived from the **Safety Score** and stays put. `Review` as a **Recommendation** is the Skill's verdict on one **Update**; `Review` as a **Band** is the 50–74 score bucket. Same word, two concepts — don't conflate them."
> **Dev:** "So if an **Update** lands in the **Risky** Band but the Skill is confident, how does it get installed?"
> **Domain expert:** "The Skill writes an **Annotation** with `Approve`. That un-comments the line in the **Apply Script** even though the Band is Risky. It can also set an **Adjusted Score** so the **Report** reflects its real assessment."
> **Dev:** "And winget calls these 'upgrades' — should the code say upgrade?"
> **Domain expert:** "Only when quoting the literal winget command or output. Everywhere else the domain entity is an **Update**. Keep it consistent."

## Flagged ambiguities

- **"Review" has three meanings.** (1) a **Band** (Safety Score 50–74), (2) a **Recommendation** value (`Approve`/`Review`/`Skip`), and (3) the user's act of reading the **Report** before **Approval**. Always qualify it — "Review band" vs "Review recommendation" — in code and prose.
- **"Update" vs "upgrade".** The domain entity is an **Update**. Use "upgrade" *only* when quoting the literal `winget upgrade` command or its raw output; never as the domain noun.
- **"Score" is two things.** The engine's **Safety Score** (deterministic) vs the Skill's **Adjusted Score** (an Annotation override). Name the one you mean; never bare "score" in code touching both.
- **"Run" verb vs noun.** A **Run** (noun) is the result of a **Scan**. Use **Scan** for the action; reserve "run" the verb for *running the Apply Script*.
