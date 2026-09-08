# Decisions taken during execution

Every deviation from the approved blueprint, written **before** the code that implements it.
Deviating without an entry here is drift and ends the run.

## ADR-R01 - Development database moves from LocalDB to the running SQL Express instance

**Phase:** P02 · **Date:** 2026-09-08 · **Status:** Accepted

**Context.** ADR-00 chose SQL Server LocalDB as the development instance and named a running
SQL Express service as the permitted alternative. During the P02 gate, LocalDB became
unreachable from every client on this machine:

```
sqllocaldb info MSSQLLocalDB
  State:              Running
  Version:            15.0.4382.1
  Instance pipe name: np:\\.\pipe\LOCALDB#5A96C462\tsql\query

direct-pipe => FAIL => provider: Named Pipes Provider, error: 40 - Could not open a connection
express     => OK   => Microsoft SQL Server 2025 (RTM) - 17.0.1000.7
localdb     => FAIL => provider: Named Pipes Provider, error: 40 - Could not open a connection
```

The instance reports Running and its own published pipe still refuses connections, so this is
not a start-up race. The same probe reaches `.\SQLEXPRESS` immediately.

**Options considered.**

1. **Keep fighting LocalDB** - delete and recreate the instance. Rejected: recreating it would
   destroy the instance state without a diagnosis, and a database engine that intermittently
   refuses its own named pipe cannot be the foundation of twelve more gated phases.
2. **Use the running SQL Express service.** Chosen. It is already running, so it costs no
   additional memory beyond what the machine was already spending, it is reachable, and it is
   SQL Server 2025 rather than LocalDB's 2019 engine, which puts development closer to a modern
   production target rather than further from it.
3. **SQL Server in Docker.** Rejected by the machine contract: Docker Desktop with WSL2 reserves
   two to four gigabytes before a container starts, on a machine with 7.9 GB in total.

**Decision.** `environment.dbInstance` becomes `.\SQLEXPRESS`. Every connection string still
carries `TrustServerCertificate=True`. The database name is unchanged: `SoftwareManagementDb`,
with `SoftwareManagementDb_Tests` for the integration suite.

**Consequences.**

- The development engine is now SQL Server 2025, so the compatibility floor recorded in
  `08-environment.md` (write nothing that requires a version newer than the development engine)
  becomes easier to satisfy, not harder. No 2025-only syntax will be used regardless, because
  production is planned as SQL Server 2022 or Azure SQL.
- Express idles at roughly 200 to 500 MB. It was already running before this run started, so
  the local footprint budget in `08-environment.md` is unchanged: the budget already counted it.
- This run never starts or stops the Express service. It was running before and is left exactly
  as it was found.
- Reversal cost: one connection string. Nothing in the code depends on which instance answers.

**Recorded as:** ASM-1 in `ASSUMPTIONS.md`, and `STATE.json.environment.dbInstance`.

## ADR-R02 - Identity entities live in the Domain project

**Phase:** P03 · **Date:** 2026-09-08 · **Status:** Accepted

**Context.** `AdminUser` must derive from `IdentityUser<Guid>` for ASP.NET Core Identity to own
password hashing, lockout and the user store. That type lives in
`Microsoft.Extensions.Identity.Stores`, so putting the entity in the Domain project puts a
framework package there.

**Options considered.**

1. **Identity entities in Domain, referencing `Microsoft.Extensions.Identity.Stores`.** Chosen.
   The package is an abstraction package: it carries the user, role and token store interfaces
   and no persistence technology. The architecture test still proves the boundary that matters,
   that Domain references no Entity Framework Core assembly and no other project in the solution.
2. **A separate persistence-only `ApplicationUser` in Infrastructure, with a pure domain twin.**
   Rejected: two types for one row, a mapping layer between them, and every identity operation
   would have to translate. That cost buys purity nobody consumes, because nothing in this system
   will ever swap ASP.NET Core Identity for another identity library without also rewriting the
   endpoints.
3. **Skip ASP.NET Core Identity and hand-roll password hashing and lockout.** Rejected outright:
   writing our own password hashing is exactly the kind of shortcut that turns into a breach.

**Decision.** `AdminUser`, `AdminRole`, `Permission`, `RolePermission`, `RefreshToken` and
`LoginAttempt` live in `SoftwareManagement.Domain.Identity`, and the Domain project references
`Microsoft.Extensions.Identity.Stores` and nothing else from the framework.

**Consequences.** The architecture test is tightened rather than relaxed: it now asserts that the
only framework package Domain references is the identity abstraction, so a future EF Core or
ASP.NET Core MVC reference in Domain still fails the build.

## ADR-R03 - The test gate retries a Smart App Control block, and nothing else

**Phase:** P03 · **Date:** 2026-09-08 · **Status:** Accepted

**Context.** Windows Smart App Control is enforced on this machine
(`VerifiedAndReputablePolicyState = 1`). Every build produces unsigned assemblies with new
hashes, and until the reputation lookup for those hashes resolves, the test host is refused
permission to load them:

```
Event 3077, Policy ID {0283ac0f-fff1-49ae-ada1-8a933130cad6}
Code Integrity determined that a process (testhost.exe) attempted to load
SoftwareManagement.Domain.dll that did not meet the Enterprise signing level requirements
```

The condition is transient and was proved so: a run that failed with 31 blocked loads passed
with no change at all a minute later, and the identical assemblies copied to another drive
passed immediately. A full clean rebuild did not prevent it, so it is not stale output.

**Options considered.**

1. **Turn Smart App Control off.** Rejected, and not something this run will do. It is a
   machine-wide malware protection, and switching it off is one-way: re-enabling it requires
   resetting Windows. Weakening the owner's security posture irreversibly to make a test run
   convenient is not a trade this build gets to make on his behalf.
2. **Sign the assemblies.** Rejected: it needs a code-signing certificate the project does not
   have, and a self-signed certificate does not satisfy Smart App Control anyway.
3. **Retry only this specific condition.** Chosen.

**Decision.** `scripts/run-tests.ps1` runs the suite and retries **only** when the output
contains `An Application Control policy has blocked this file`, waiting 20 seconds between
attempts, at most four attempts. A genuine test failure is returned immediately and is never
retried. The script prints `TEST_ATTEMPTS` and `SAC_RETRIES` so every phase report states how
many retries the environment needed, and the run fails loudly if the block persists.

**Consequences.**

- No test is skipped, no assertion is weakened and no failure is hidden: the retry is scoped to
  an operating-system condition that has nothing to do with the code under test.
- Phase reports carry the retry count, so if this ever starts masking something the evidence is
  already in the record.
- If the owner later chooses to exclude the repository from Smart App Control himself, the retry
  simply never fires.
