# Approved exceptions

A banned pattern (see the anti-stub contract, section 11 of `PROTOCOL.md`) may appear in
`src/**` only on a line carrying `// APPROVED-EXCEPTION: EXC-<n>` with a matching row in the
table below. Hard cap for the whole run: 5.

Patterns that are never exceptable, whatever the justification: `NotImplementedException`,
`Assert.True(true)`, `Assert.Pass()`, `expect(true)`, `[Fact(Skip=...)]`, `[Ignore]`,
`xit(`, `fit(`.

| EXC-ID | File:line | Pattern | Why no alternative exists | Phase | Permanent or remove by |
|---|---|---|---|---|---|
| EXC-01 | `backend/src/SoftwareManagement.Infrastructure/Persistence/Migrations/20260907235012_InitialCreate.Designer.cs:20` and `.../AppDbContextModelSnapshot.cs:17` | `#pragma warning disable 612, 618` | Emitted by EF Core itself in generated migration output. An applied migration is frozen: a mistake is corrected by a new migration, never by hand-editing the old one, so the line cannot be removed without editing generated code that the tool will rewrite anyway. The protocol's own carve-out for generated migrations covers exactly this case. | P02 | Permanent while EF Core emits it |
| EXC-02 | `.editorconfig:67` scoped to `backend/src/SoftwareManagement.Domain/Identity/Permission.cs` | `dotnet_code_quality.CA1711.allowed_suffixes = Permission` | CA1711 reserves the suffix `Permission`. The two types that carry it are the names the approved data model (E-43, E-44) and the authorization matrix use, and the seeded table is `Permissions`; renaming them would make the code disagree with the document it implements. Scoped to the single file that declares them, so the analyser still fails the build for a reserved suffix anywhere else. | P03, narrowed and recorded in P07 (ADR-R06) | Permanent while those names stand |

## Secret-scan pathspec exclusions

The secret scan's pattern matches `Password = "value"`. One file legitimately contains that shape
without holding a secret, so that single file is excluded by pathspec. The pattern itself is never
relaxed, because a weaker pattern would stop finding real secrets everywhere else.

| File | Line | Why it matches | Why it is not a secret |
|---|---|---|---|
| `backend/src/SoftwareManagement.Domain/Identity/LoginAttempt.cs` | 31 | `public const string WrongPassword = "wrong_password";` | It is the audit-log reason code recorded against a failed sign-in. The value is written to the `LoginAttempts` table and never returned to a caller; it grants nothing. |

### P11: the rest of the pathspec, written down

Until P11 the scan was retyped each phase and its pathspec was not recorded anywhere, which is the
same drift `run-gate.ps1` was written to stop. Running the pattern exactly as `AGENTS.md` §12 writes
it surfaced twenty hits across four groups. None is a secret; all four are recorded here so the next
run has one list rather than a fresh judgement call.

| Excluded path | Hits | Why it matches | Why it is not a secret |
|---|---|---|---|
| `backend/tests/*` | 13 | `public const string Password = "Fixture-Pass-2026";` and the request bodies the authentication tests post | Credentials for databases created and dropped by the test run on the developer's own SQL Server instance. No deployed system accepts them, and a test that signs in has to know the password it just set. |
| `scripts/*` | 3 | `$env:Jwt__Key = "gate-signing-key-not-a-secret-$Nonce-..."` and `$env:Seed__OwnerPassword = $ownerPassword` | Generated fresh per run for the throwaway gate database and never written to a file. The owner password is a new GUID each time; the signing key is literally named for what it is not. |
| `backend/src/**/Persistence/Migrations/*` | 1 | A generated column definition named `DemoPassword` | EF Core's own output, describing a column. It holds no value. |
| `backend/src/SoftwareManagement.Api/Controllers/ProductsController.cs` | 1 | `product.Demo.DemoPassword = body.DemoPassword;` | An assignment from a request body, not a literal - the pattern allows an unquoted right-hand side, so a property read matches it. The value it moves is a shared demo credential the product page shows on purpose (A-20). |

The pattern is not relaxed and no group is dropped: the exclusions are paths, so a real key
committed to `backend/src` outside that one controller still fails the gate.

Count in force against the `src/**` cap: 2 of 5 (`LoginAttempt.cs`, `ProductsController.cs`). The
other three exclusions are outside `src/**`.

**How the scan treats this.** The detector is not edited and the pattern list is not shortened.
The backend scan excludes `**/Persistence/Migrations/**` because those files are generated and
frozen, and it is asserted separately that this exclusion covers only generated migrations: any
`#pragma warning disable` in hand-written source is still a gate failure.

If a correct implementation is genuinely impossible, the right action is a `BLOCKERS.md` entry
and a loud line in the phase report. A silent stub is worse than an unfinished phase.
