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

## ADR-R04 - Sales reads the product catalogue; the phase plan's exit line was wrong

**Status.** Accepted, P05, 2026-09-08.

**Context.** The P05 exit criteria table says: "Sales token against `/api/v1/admin/products` => 403".
The first authorization test written against that line failed, because the endpoint answered 200.
The frozen authorization matrix explains why: AZ-12 grants `catalog.product.read` to Anonymous
(published only), Editor, Sales, Auditor and Owner, and AZ-13 and AZ-18 withhold the product and
plan writes from Sales. The role map in `RolePermissionMap` already matched the matrix.

The two documents disagree, and one of them had to give.

**Options considered.**

1. **Change the code so Sales gets 403 on the read.** Rejected. It would contradict AZ-12, and it
   would be wrong on its own terms: Sales quotes products to prospects, so a salesperson who cannot
   open the catalogue cannot do the job. It would also have to be undone in P09, where a quote line
   item names a product.
2. **Weaken or delete the failing assertion.** Rejected outright. R-13 forbids it, and a phase that
   proves nothing about authorization is worse than one that proves the wrong thing loudly.
3. **Treat the authorization matrix as binding and correct the assertion.** Chosen.

**Decision.** `06-authz.md` is the authority on who may do what. The P05 gate asserts both halves of
the rule rather than one: Sales gets 200 on `GET /api/v1/admin/products` (AZ-12) and 403 on both
`POST /api/v1/admin/products` (AZ-13) and `POST .../plans` (AZ-18), and the database is checked
afterwards to confirm the refused write left nothing behind. The demo-credentials test follows the
same matrix: AZ-19 grants the credentials to Owner, Editor and Sales, and withholds them from the
Auditor, which is the refusal that test now asserts.

**Consequences.**

- The exit criteria table in `09-phase-plan.md` keeps a line that is looser than the matrix. It is
  left as written, because the phase plan is frozen; this record says which document wins.
- A role that can do neither half is now as visible a failure as one that can do both, because both
  are asserted in the same test.
- `ContentFixture` seeds an Auditor alongside the Owner, Editor and Sales, so a read-only role can
  be asserted directly rather than inferred.

## ADR-R05 - The server-rendering process forwards /api to the API

**Status.** Accepted, P06, 2026-09-08.

**Context.** The public site renders on the server so that a search engine sees real content
(NFR-SEO-01). It was not doing that. A component asks for a relative URL, `/api/v1/public/products`.
In a browser that resolves against the site's own origin and reaches the API. During server-side
rendering it resolved against the rendering server, which answered with a rendered HTML page; the
component received HTML where it expected JSON, treated the response as a failure, and rendered its
empty state.

Every public page had been served to crawlers as an empty shell since P04. The render proofs of P04
and P05 reported PASS because their markers were `data-testid` attributes on section wrappers, which
appear in the HTML whether or not the page reached the API.

**Options considered.**

1. **Give the renderer an absolute API address through a token.** Rejected. It works, but it means
   two addresses for the same API, one for the browser and one for the server, that have to be kept
   in step through every deployment; and the first time they disagree the symptom is exactly the one
   above, silence rather than an error.
2. **Point the renderer at the API and leave the browser to a separate origin.** Rejected: it needs
   cross-origin configuration, a second hostname and a cookie policy that spans both, all to avoid
   a forwarder that is a dozen lines.
3. **Forward `/api` from the rendering process to the API.** Chosen.

**Decision.** `frontend/src/server.ts` forwards everything under `/api` to `API_BASE_URL` when that
variable is set. The body is streamed rather than buffered, so an upload passes through without
sitting in memory; hop-by-hop headers are dropped; the original host is passed on as
`X-Forwarded-Host`; and an unreachable API is a 502 from this process rather than a rendering
failure. When the variable is unset nothing is forwarded, which is the right behaviour behind a
reverse proxy that already does it.

**Consequences.**

- The renderer and the browser resolve the same relative URL to the same place, which is the whole
  point: there is one address, not two that can drift.
- The render proof's markers are now content rather than test ids, and the script says why. A marker
  that a component can emit without any data proves the route rendered and nothing else.
- The forwarder is a single point every API call passes through in this deployment shape. It is
  deliberately dumb: it adds no caching, no retries and no rewriting, so there is nothing in it to
  get subtly wrong.

## ADR-R06 - The CA1711 suffix allowance is narrowed, recorded, and the guardrail hash re-frozen

**Status.** Accepted, P07, 2026-09-20.

**Context.** The P07 gate's guardrail check (G0, R-13) reported `.editorconfig` CHANGED. The change
was not made in P07. `.editorconfig` was last modified in P03 (`4c9f7c2`), and the hash frozen in
`STATE.json` is still the P02 one. So the drift guard on this file has been comparing against a
stale value since P03 and could not have fired in P04, P05 or P06: the one check whose job is to
notice a loosened guardrail was itself broken for four phases.

Two things were changed in P03, and they are not the same kind of change.

The first is `generated_code = true` on the `Migrations` path section. That is the protocol's own
carve-out for generated EF output expressed in the only file that can express it: a
`Directory.Build.props` placed in a project subfolder is never read, because MSBuild searches
upward from the project file, so the documented approach silently does nothing there. The scope is
identical to the carve-out already covered by EXC-01.

The second is not a carve-out. `[backend/src/**.cs]` with
`dotnet_code_quality.CA1711.allowed_suffixes = Permission` relaxes an analyser across **all**
hand-authored backend source, and it was made with no ADR and no `EXCEPTIONS.md` row. Under R-13
that is a loosened warning level, and the justification written in the file is not the record the
protocol asks for.

The justification itself is sound. CA1711 reserves the suffix `Permission`, and exactly two types
carry it: `Permission` and `RolePermission`, both in
`backend/src/SoftwareManagement.Domain/Identity/Permission.cs`. Those are the names the approved
data model (E-43, E-44) and the authorization matrix use, and the seeded table is `Permissions`.
Renaming them to satisfy a naming heuristic would make the code disagree with the document it
implements, which is a worse outcome than the suffix.

**Options considered.**

1. **Rename the types.** Rejected. It buys nothing and breaks the correspondence between the code
   and the approved data model, which is the thing that makes the blueprint worth having.
2. **Leave the relaxation as it is and only re-freeze the hash.** Rejected. It would record the
   drift without correcting it, and it leaves an analyser disabled across every backend source file
   to serve two type names.
3. **Narrow the relaxation to the file that needs it, record it, re-freeze.** Chosen.

**Decision.** The CA1711 section is scoped to
`[backend/src/SoftwareManagement.Domain/Identity/Permission.cs]` rather than `[backend/src/**.cs]`,
so any other type that acquires a reserved suffix anywhere else still fails the build. An
`EXCEPTIONS.md` row is added for it. `STATE.json.guardrailHashes[".editorconfig"]` is re-frozen to
the corrected file, and the P07 gate records both the old and the new value so the re-freeze is
visible rather than silent.

**Consequences.**

- The drift guard on `.editorconfig` works again from P07 onward. A stale frozen hash is
  indistinguishable from a passing check, which is why this is written down rather than fixed
  quietly.
- Re-freezing a guardrail hash is legal only with an ADR saying why, and this is the precedent for
  that: the gate prints `GUARD_REFROZEN` with both hashes and the ADR id, or it fails.
- The analyser relaxation now covers one file and two types instead of the whole backend. If a
  third reserved suffix appears, the build fails and the decision is taken again on purpose.
