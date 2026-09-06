# 08 Environment

## §5 Preflight Measurements
- **.NET SDK**: 10.0.400
- **.NET Runtime**: Microsoft.AspNetCore.App 8.0.30, 9.0.19, 10.0.11
- **Node**: v24.13.0
- **npm**: 11.6.2
- **Angular CLI**: 21.1.1
- **Git**: git version 2.52.0.windows.1
- **SQL Server Services**: MSSQL$SQLEXPRESS (Running)
- **EF Tooling**: Entity Framework Core .NET Command-line Tools 10.0.11
- **Available MB at Phase Start**: 1910 MB (highly constrained)

## Local Footprint Budget
Total limit: **6.5 GB** (Machine has 8GB RAM, OS takes ~1.5-2GB, Chrome/VS Code take ~1.5GB).

| Process | Expected RAM | Simultaneous? | Alternative if over |
|---|---|---|---|
| OS / Antimalware / Background | 2500 MB | Yes | N/A |
| Chrome / VS Code (User) | 1500 MB | Yes | Close tabs/extensions. |
| SQL Server LocalDB | 300 MB | Yes | Shut down local Express instance. |
| .NET Web API (run) | 150 MB | Yes | `dotnet watch` off (use published build). |
| Angular Build / Dev Server | 800 MB | No | Limit Node heap via `NODE_OPTIONS=--max-old-space-size=1024`. Run build, not serve. |
| .NET Build / MSBuild | 600 MB | No | `MSBUILDDISABLENODEREUSE=1` and `-m:1`. |
| Angular Test (Karma/Chrome) | 800 MB | No | Use Headless Chrome, shut down other builds. |

**Total Simultaneous Peak**: 2500 (OS) + 1500 (User) + 300 (DB) + 150 (API) + 800 (Angular Build) = 5250 MB.
This fits well within the 6.5 GB constraint, leaving ~1.2 GB headroom for unexpected spikes.

## Strict Rules Enforced by this Budget
1. **Never run `ng serve` and `dotnet watch` simultaneously.**
2. **Never run a frontend build (`ng build`) concurrently with a backend build (`dotnet build`).**
3. **Database MUST be SQL Server LocalDB or Express natively.** Docker Desktop / WSL2 are strictly forbidden as they reserve 2-4GB on startup, instantly blowing the budget.
