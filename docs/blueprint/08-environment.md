# 08 Environment

Everything below was printed by a command on this machine on 2026-09-07/08. Nothing is remembered.

## Preflight output (pasted)

```
=== dotnet --list-sdks
10.0.400 [C:\Program Files\dotnet\sdk]

=== dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App"
Microsoft.AspNetCore.App 8.0.30 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.AspNetCore.App 9.0.19 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.AspNetCore.App 10.0.11 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]

=== node --version / npm --version / git --version
v24.13.0
11.6.2
git version 2.52.0.windows.1

=== dotnet tool restore; dotnet dotnet-ef --version
10.0.11

=== Memory and machine
AVAILABLE_MB=2779
TOTAL_RAM_GB=7.9

=== SQL Server
Name              Status
----              ------
MSSQL$SQLEXPRESS  Running

HAS_SQLLOCALDB=True
SQLCMD_PATH=C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE
LocalDB instance "mssqllocaldb" started.
DB-OK | Microsoft SQL Server 2019 (RTM-CU27-GDR) (KB5040948) - 15.0.

=== Disk
Name FreeGB
---- ------
G        54
C        66

=== Tooling presence
python  -> "Python was not found; run without arguments to install from the Microsoft Store"
           (the Windows Store execution-alias stub, NOT a usable interpreter)
gh      -> NOT INSTALLED
rg      -> NOT INSTALLED
jq      -> NOT INSTALLED
chrome  -> present at C:\Program Files\Google\Chrome\Application\chrome.exe

=== npm package versions used for grounding (full list in 01-research.md)
@angular/cli 22.1.7 · @angular/core 22.1.5 · @angular/material 22.1.5 (MIT) · @angular/ssr 22.1.7
```

`PREFLIGHT: PASS`

## What these measurements change

| Measurement | Consequence for the build |
|---|---|
| 7.9 GB total RAM, 2779 MB available with the editor open | One heavy build at a time. Never a .NET build and an Angular build together, never two servers. The Node heap is sized from `Available MBytes` at each gate, not from a fixed number. |
| SQL Server 2019 through LocalDB `(localdb)\MSSQLLocalDB` | The development target is SQL Server 2019 compatibility level. No SQL Server 2022-only syntax (for example `GENERATE_SERIES` or `JSON_OBJECT`) may appear in a migration or a query, even though production may run 2022. This is a real constraint that would otherwise be discovered at deployment. |
| `MSSQL$SQLEXPRESS` is also running | It is left alone. LocalDB is the instance of record (ADR-00). Express idling costs 200-500 MB, which is why nothing is moved to it. |
| `sqlcmd` v18 is present | Usable, but every gate still goes through the `Invoke-Sql` SqlClient helper so that a machine without `sqlcmd` reproduces the build. `sqlToolPath = "SqlClient"`. |
| Python is a Store stub | No `.py` scripts, no `pip`, no `git filter-repo`. Every helper script in this repository is PowerShell, Node or Bash. |
| `gh`, `rg`, `jq` absent | Git only for GitHub work; `Select-String` and `ConvertFrom-Json` for parsing. No gate may depend on them. |
| Chrome present | A browser runner would work, but ADR-14 still chooses Vitest to keep a Chrome process out of a memory-bound gate. |
| G: has 54 GB free, C: has 66 GB | Repository, backups (`G:\_backups`) and the acceptance clone (`G:\_acceptance`) all fit with room to spare. |
| ASP.NET Core 8, 9 and 10 runtimes side by side | The application pins `net10.0` explicitly in every csproj so a stray global.json or an implicit rollforward cannot silently build against 8 or 9. |

## Local footprint budget

Total must stay under 6.5 GB so the machine never swaps during a gate.

| Process | Expected RAM | Runs at the same time as? | Alternative if over budget |
|---|---|---|---|
| Windows and background services | 1.6 GB | always | nothing to trade |
| SQL Server LocalDB (`sqlservr.exe`) | 300-500 MB | always during a gate | cap `max server memory` at 512 MB |
| `MSSQL$SQLEXPRESS` (already running, unused) | 200-500 MB | always | stop the service manually before a heavy gate; the protocol never stops a service it did not start |
| .NET build (`MSBuild` with `-m:1 -nodeReuse:false`) | 700 MB-1.2 GB | alone; never with an Angular build | `dotnet build-server shutdown` between steps |
| `dotnet test` with coverage | 600-900 MB | alone | run per test project instead of the whole solution |
| Published API under smoke test | 250-400 MB | with the static server only | none needed |
| Angular production build (Node) | 1.2-2.5 GB, heap capped from `Available MBytes` | alone | `NODE_OPTIONS=--max-old-space-size` computed as `min(3072, max(1024, available - 900))` |
| Vitest run | 500-800 MB | alone | shard by feature folder |
| Static file server for the render proof | 60 MB | with the published API | none needed |
| Editor and browser (the owner's) | 1-2 GB | closed during heavy gates | close Chrome and the editor before P02, P06 and P11 |
| **Peak during the heaviest gate step** | **about 5.6 GB** | Angular build alone with LocalDB and Windows | stop `MSSQL$SQLEXPRESS` to recover 200-500 MB |

Gate ceilings from the machine contract: build 15 minutes, test 10 minutes, `npm ci` 10 minutes, `ng build` 15 minutes, `ng test` 10 minutes. A hang past a ceiling is a gate failure, and the first thing to re-check is `Available MBytes`, not the source code.

## Ports

| Port | Use | Freed by |
|---|---|---|
| 5199 | Published API during the phase gate | G1 identity-checked reclaim, then the `finally` block of G6 |
| 4300 | Static server for the production Angular bundle | the `finally` block of G7 |
| 5299 | API during final acceptance against the clean clone | the acceptance teardown |
| 4399 | Static server during final acceptance | the acceptance teardown |

A port held by a process this run did not start is never killed; the port number is changed in `STATE.json` instead and the event is recorded as an assumption.

## Paths

| Purpose | Path |
|---|---|
| Repository root | `G:\software-management` |
| Backups | `G:\_backups\software-management` |
| Acceptance clone | `G:\_acceptance\software-management-<timestamp>` |
| Evidence | `G:\software-management\_evidence\phase-NN` |
| Published API | `G:\software-management\_publish\api` |
| Angular dist | `G:\software-management\frontend\dist\<app>\browser` |

No path contains a space and none is a drive root.

## Environment variables the build expects

Documented by name, never by value. Full table in `docs/ENVIRONMENT.md` once P01 creates it.

| Name | Purpose | Where it lives in development |
|---|---|---|
| `ConnectionStrings__Default` | SQL Server connection | user-secrets |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Token signing | user-secrets |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__FromAddress` | Outbound mail | user-secrets |
| `Turnstile__SiteKey`, `Turnstile__SecretKey` | Bot protection | user-secrets |
| `SMOKE_SEED_PASSWORD`, `SMOKE_LOWPRIV_PASSWORD` | Gate smoke logins | process environment, loaded from user-secrets before the gate |
| `ASPNETCORE_ENVIRONMENT` | Configuration selection | set per command, cleared afterwards by the gate |

Committed configuration files carry empty placeholders only. The secret scan runs over the staged index before every commit, and a hit stops the line.
