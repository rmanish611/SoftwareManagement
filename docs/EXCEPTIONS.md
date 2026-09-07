# Approved exceptions

A banned pattern (see the anti-stub contract, section 11 of `PROTOCOL.md`) may appear in
`src/**` only on a line carrying `// APPROVED-EXCEPTION: EXC-<n>` with a matching row in
the table below. Hard cap for the whole run: 5.

Patterns that are never exceptable, whatever the justification: `NotImplementedException`,
`Assert.True(true)`, `Assert.Pass()`, `expect(true)`, `[Fact(Skip=...)]`, `[Ignore]`,
`xit(`, `fit(`.

| EXC-ID | File:line | Pattern | Why no alternative exists | Phase | Permanent or remove by |
|---|---|---|---|---|---|
| - | - | - | none used so far | - | - |

Count in force: 0 of 5.

If a correct implementation is genuinely impossible, the right action is a `BLOCKERS.md`
entry and a loud line in the phase report. A silent stub is worse than an unfinished phase.
