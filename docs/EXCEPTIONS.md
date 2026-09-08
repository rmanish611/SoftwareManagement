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

Count in force: 1 of 5.

**How the scan treats this.** The detector is not edited and the pattern list is not shortened.
The backend scan excludes `**/Persistence/Migrations/**` because those files are generated and
frozen, and it is asserted separately that this exclusion covers only generated migrations: any
`#pragma warning disable` in hand-written source is still a gate failure.

If a correct implementation is genuinely impossible, the right action is a `BLOCKERS.md` entry
and a loud line in the phase report. A silent stub is worse than an unfinished phase.
