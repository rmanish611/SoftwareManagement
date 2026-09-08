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
