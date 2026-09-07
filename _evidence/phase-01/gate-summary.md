# P01 gate evidence

Nonce ad618526. Scans run against the staged index before the phase commit.

- BLUEPRINT_METRICS=PASS (all 12 floors met, Must 71% inside the 55-75% band, all 7 source classes present)
- GATE_H PASS failures=0 warnings=35
- D11 anti-stub: SCANNED_FILES=6 STUB_HITS=0 EMPTY_CATCH=0 WEAK_TESTS=0 FE_BANNED_HITS=0 (no application code exists yet by design; P01 writes only repository guardrails)
- Build output tracked in git: none
- SECRET_SCAN_EXIT=1 (clean). The first run exited 128 because the pathspec used `:!path` syntax this git version rejects. Per R-28 a detector that errors is a failed gate, so the detector was fixed to `:(exclude)path` and re-run, and a canary line was staged to prove the detector still detects (CANARY_EXIT=0) before being removed.
