# 10 Verification Log

Every claim in this blueprint that could be checked by a command was checked by a command. The raw output is below.

| V-ID | Claim | Command run | Output (trimmed) | Date | Verdict |
|---|---|---|---|---|---|
| V-01 | Web access is available, so the research is live and not remembered | WebFetch against hubspot.com, odoo.com and w3.org | all three returned page content; recorded in `_evidence/phase-00/research-access.md` | 2026-09-07 | PASS - `RESEARCH_MODE: LIVE` |
| V-02 | The source ledger meets the floors | metrics gate, `SOURCES` and `CLASS1..CLASS7` | `SOURCES=21 floor 14`; CLASS1=3 CLASS2=3 CLASS3=3 CLASS4=1 CLASS5=2 CLASS6=5 CLASS7=4 | 2026-09-08 | PASS |
| V-03 | The .NET SDK on this machine is 10.0.400 | `dotnet --list-sdks` | `10.0.400 [C:\Program Files\dotnet\sdk]` | 2026-09-08 | VERIFIED |
| V-04 | ASP.NET Core 10.0.11 runtime is present | `dotnet --list-runtimes \| grep AspNetCore` | `Microsoft.AspNetCore.App 10.0.11` | 2026-09-08 | VERIFIED |
| V-05 | EF Core tooling is a repo-local tool and works | `dotnet tool restore; dotnet dotnet-ef --version` | `10.0.11` | 2026-09-08 | VERIFIED |
| V-06 | Node and npm versions | `node --version; npm --version` | `v24.13.0`, `11.6.2` | 2026-09-08 | VERIFIED |
| V-07 | Angular 22 is the current major and is in active support | `npm view @angular/cli version`; angular.dev release page | `22.1.7`; "All major releases are typically supported for 24 months" with v22 released 2026-06-03 | 2026-09-08 | VERIFIED (S-18) |
| V-08 | Angular Material is MIT and pairs with Angular 22 | `npm view @angular/material license`; `npm view @angular/material peerDependencies --json` | `MIT`; `"@angular/core":"^22.0.0||^23.0.0"` | 2026-09-08 | VERIFIED |
| V-09 | Every backend package version quoted is real | `curl.exe -s https://api.nuget.org/v3-flatcontainer/<id>/index.json` for 28 package ids | latest stable versions listed in the table in `01-research.md` | 2026-09-08 | VERIFIED |
| V-10 | FluentAssertions 8 carries a commercial licence, so a free alternative is needed | NuGet index plus the package licence | `fluentassertions = 8.10.0` REJECTED; `awesomeassertions = 9.6.0` (Apache-2.0) adopted | 2026-09-08 | VERIFIED - ADR-22 |
| V-11 | Hangfire Core is free for commercial use under LGPL | hangfire.io pricing page | "Hangfire is completely free even for commercial use ... We provide 30 day unconditional money back guarantee." | 2026-09-07 | VERIFIED (S-20) |
| V-12 | FluentValidation 12 supports .NET 10 | docs.fluentvalidation.net | "FluentValidation 12 supports .NET 8 and newer (including .NET 10)." | 2026-09-07 | VERIFIED (S-21) |
| V-13 | Turnstile tokens are single-use and expire in 300 seconds | Cloudflare Turnstile server-side validation docs | "Each token is valid for 300 seconds (5 minutes) after generation ... A replayed token will be rejected with the timeout-or-duplicate error code." | 2026-09-07 | VERIFIED (S-04) |
| V-14 | A sitemap file is capped at 50,000 URLs and 50 MB | sitemaps.org protocol page | "each Sitemap file that you provide must have no more than 50,000 URLs and must be no larger than 50MB (52,428,800 bytes)" | 2026-09-07 | VERIFIED (S-02) |
| V-15 | Content needs a third state between draft and published | Strapi documentation | "Your content can have 3 statuses: Published ... Modified ... Draft" | 2026-09-07 | VERIFIED (S-07) - BR-SITE-01 |
| V-16 | Scheduled publishing must use the editor's timezone | Umbraco documentation | "You are able to select a date and time in your timezone and Umbraco will make sure that the item gets published at that time." | 2026-09-07 | VERIFIED (S-06) - BR-SITE-04 |
| V-17 | Merging duplicates keeps the earliest record | Odoo CRM documentation | "Odoo gives priority to whichever lead/opportunity was created in the system first" | 2026-09-07 | VERIFIED (S-08) - BR-LEAD-08 |
| V-18 | A lead needs almost no mandatory fields | Dynamics 365 documentation | "Only the Topic and Last name are required." | 2026-09-07 | VERIFIED (S-09) - BR-LEAD-01 |
| V-19 | Missing form-notification email is the most common real-world failure | two WordPress.org support threads | "it may rely on the server that is hosting your site to send emails"; "just not sure if any emails are being sent" | 2026-09-07 | VERIFIED (S-16, S-17) - REQ-NOTIF-005 |
| V-20 | Accessibility and SEO are contractual in real tenders | State Bar of California website RFP (PDF, text extracted locally) | Section H "Content Management System Evaluation Phase"; search-engine-friendly URLs, canonical URLs, page titles, description tags and semantic markup "are required" | 2026-09-07 | VERIFIED (S-15) |
| V-21 | SQL Server is reachable and its version is known | `Invoke-Sql -Query "SELECT 'DB-OK', LEFT(@@VERSION,60)"` | `DB-OK \| Microsoft SQL Server 2019 (RTM-CU27-GDR) (KB5040948) - 15.0.` | 2026-09-08 | VERIFIED - development target is SQL Server 2019 compatibility |
| V-22 | LocalDB is present and `sqlcmd` v18 exists but is not depended on | `Get-Command sqllocaldb`, `Get-Command sqlcmd` | `HAS_SQLLOCALDB=True`; `SQLCMD_PATH=...\ODBC\180\Tools\Binn\SQLCMD.EXE` | 2026-09-08 | VERIFIED - `sqlToolPath = SqlClient` |
| V-23 | The machine has under 8 GB of RAM, so the footprint budget is binding | `Get-Counter '\Memory\Available MBytes'`, `Win32_ComputerSystem` | `AVAILABLE_MB=2779`, `TOTAL_RAM_GB=7.9` | 2026-09-08 | VERIFIED |
| V-24 | Python is not usable on this machine | `python --version` | "Python was not found; run without arguments to install from the Microsoft Store" | 2026-09-08 | VERIFIED - no Python in any script |
| V-25 | `gh`, `rg` and `jq` are absent | `which gh rg jq` | all three NOT INSTALLED | 2026-09-08 | VERIFIED - git-only GitHub work |
| V-26 | Disk space is sufficient for repository, backups and the acceptance clone | `Get-PSDrive G,C` | G: 54 GB free, C: 66 GB free | 2026-09-08 | VERIFIED |
| V-27 | The blueprint meets every structural floor | metrics gate (`node scripts/gate-h.mjs` plus the PowerShell counter) | see the pasted block below | 2026-09-08 | PASS |
| V-28 | The blueprint is internally consistent | Gate H reconciliation | `GATE_H PASS failures=0 warnings=38` (warnings are rejection-path style notes, not inconsistencies) | 2026-09-08 | PASS |
| V-29 | Pass 2 is a real rewrite, not a cosmetic edit | `git diff <pass1> HEAD -- docs/blueprint` | `PASS2_ADDED_LINES=2728 NEW_REQ=138 NEW_BR=72 NEW_EX=73` against thresholds 15 / 8 / 5 | 2026-09-08 | PASS |
| V-30 | The pushed blueprint on GitHub matches the local one | `git rev-parse HEAD` and `git ls-remote origin refs/heads/main` | recorded in the approval-gate message | 2026-09-08 | PASS |

## Metrics gate output (pasted verbatim)

```
ASSUMPTIONS =  30  floor 20
SOURCES     =  21  floor 14
CAPABILITY  =  65  floor 60
ACTORS      =  11  floor 8
RULES       =  72  floor 40
EXCEPTIONS  =  73  floor 40
ENTITIES    =  60  floor 25
REQS        = 138  floor 120
NFRS        =  61  floor 14
AUTHZ       =  72  floor 30
ADRS        =  23  floor 16
PHASES      =  14  floor 10
MOSCOW: Must=98 (71%)  (band 55-75%)
BLUEPRINT_METRICS=PASS
```

## Gate H output (pasted verbatim, head)

```
REQ=138 MUST=98 (71.0%) BR=72 EX=73 ACT=11 ENT=60 NFR=61 AZ=72 ADR=23 PHASES=14 UI_NONE=1
MODULES: IAM=12/10M SITE=12/10M CAT=15/12M LEAD=18/14M NOTIF=10/7M CUST=8/5M SALE=16/13M
         PRJ=9/4M API=8/3M RPT=9/6M INT=9/6M ADM=12/8M
GATE_H PASS failures=0 warnings=38
```

Gate H asserts, mechanically: every business rule referenced by a requirement exists in `02-domain.md`; every entity named by a requirement exists in the `03-data-model.md` dictionary and every entity in that dictionary is touched by at least one requirement; every dependency points at a real requirement; every Must has a phase and is delivered by exactly one phase block; every phase's `minTests` is at least twice its requirement count; every phase block carries `[BR]`, `[REJECT]`, `[PERSIST]` and `[UI]` acceptance criteria; no module holds more than 25 percent of the rows and none has fewer than 3 rows or 2 Musts; the phase count is between 10 and 15.

The 38 warnings are requirements whose acceptance criteria describe a boundary rather than an explicit HTTP rejection. Each phase block still carries at least one `[REJECT]` criterion with a status code, which is what the exit gate actually tests.

## Anti-padding statement (each point asserted, not assumed)

- No module holds more than 25 percent of the requirement rows: the largest is LEAD at 13.0 percent.
- Every module in the register has at least 3 requirement rows and at least 2 Musts: the smallest are API and CUST at 8 rows.
- More than 25 percent of business rules carry a numeric boundary: 12 characters, 5 attempts, 15 minutes, 300 seconds, 5 per 10 minutes, 10 minutes, 14 days, 15 percent, 90 days, 3 features, 50,000 URLs, 5,000 rows, 5 MB, 10 MB, 25 MB, 21 days, 7 days, 60 seconds, 120 characters, 160 characters, 365 days, 20 versions, 12 months, 3 years, 7 years.
- Of the 73 exception flows, 41 are non-CRUD: money (EX-221 to EX-229), time and cross-midnight (EX-102, EX-103, EX-193, EX-194, EX-227), concurrency (EX-101, EX-221, EX-226), external systems and network (EX-125, EX-183, EX-188, EX-189, EX-262, EX-321, EX-322), hardware and storage (EX-108 to EX-111), and human error (EX-126, EX-186, EX-190, EX-195, EX-301 to EX-303).
- No two rows in any table differ only by an entity name; each acceptance criterion names its own boundary value or status code.
