# Blockers

One section per blocker, per section 13 of the protocol. A blocker is durable and visible in the
repository; it is never a note in a chat message.

## BLK-1 | Phase 07 | Status: OPEN | 2026-09-20T16:05:00Z

- Signature:        `FileLoadException 0x800711C7 - An Application Control policy has blocked this file` on `bin/Release/net10.0/SoftwareManagement.Api.dll` when the integration test host loads it
- Failing command:  `dotnet test backend/SoftwareManagement.sln -c Release --no-build` (via `scripts/run-tests.ps1`)
- Error (verbatim, last lines):

```
[xUnit.net 00:00:08.41] SoftwareManagement.Api.IntegrationTests: Catastrophic failure:
System.IO.FileLoadException: Could not load file or assembly
'G:\software-management\backend\tests\SoftwareManagement.Api.IntegrationTests\bin\Release\net10.0\SoftwareManagement.Api.dll'.
An Application Control policy has blocked this file. (0x800711C7)
   at System.Reflection.RuntimeAssembly.GetExportedTypes()
   at Xunit.Sdk.ReflectionAssemblyInfo.GetTypes(Boolean includePrivateTypes)
No test is available in ...SoftwareManagement.Api.IntegrationTests.dll.
```

  Confirmed at the operating system rather than inferred from the runner: CodeIntegrity/Operational
  carries events 3033, 3077, 3089 and 3118 (Smart App Control Block Details) for each attempt.

- Attempts:
  1) The documented transient block (ADR-R03): retry with the existing budget.
     `scripts/run-tests.ps1 -ResultsDirectory _evidence\phase-07` -> `SAC_RETRIES=3`, blocked every attempt.
  2) The reputation lookup needs longer than 20 s: widen the wait.
     `scripts/run-tests.ps1 -MaxAttempts 6 -RetryDelaySeconds 45` -> `SAC_RETRIES=6`, blocked every attempt, ~4.5 minutes of waiting.
  3) ADR-R03 records that the identical assemblies passed immediately when copied to another drive, so change location rather than wait.
     `robocopy ...bin\Release\net10.0 C:\...\scratchpad\sac-run /E` then `dotnet vstest SoftwareManagement.Api.IntegrationTests.dll`
     -> blocked at the new path too. Smart App Control is refusing the file's content hash, not its location, so the mitigation recorded in P03 does not apply to this build.
  4) The lookup is a cloud call, so a machine with no route would block everything unsigned forever: prove the machine is online.
     `Invoke-WebRequest https://www.microsoft.com` -> 200, `https://api.github.com` -> 200. Connectivity is not the cause.
  5) Sequence rather than retry: run the rest of the gate first, then re-attempt, because the one property ADR-R03 establishes about this condition is that it resolves with time. Recorded below under `retriedInPhase`.

- Impact:           D3 and D15 cannot be evidenced in Release for `SoftwareManagement.Api.IntegrationTests` (141 of the 170 backend tests). The other three suites run clean in Release: Application 19, Architecture 7, Domain 4. **The same 141 tests pass in Debug**, five consecutive clean runs earlier in this phase, so this is an operating-system policy on one Release artefact and not a defect in the code or the tests. No REQ is unimplemented because of it; what is blocked is the Release evidence for REQ-LEAD-001..008 and REQ-NOTIF-001..004.
- Chosen path:      DEFER — the tests are neither skipped nor weakened. The Release run is re-attempted; nothing is marked green on the strength of the Debug run.
- Unblock action for Manish (ONE line): in Windows Security > App & browser control > Smart App Control, either turn it off or exclude `G:\software-management`, then the Release test host loads its own build like every other machine.
- Work continued on: G5 database, G6 published-API smoke, G7 frontend build, render proof, frontend tests and lint, G8 anti-stub. None of them loads the test host.
- retriedInPhase:   07

### Update 2026-09-21 — P07 closed with this open

Re-attempted after the rest of the gate: still blocked, same signature, on a Release build made
that day. Attempt 6, and the last one this phase.

The owner's instruction is to stop spending time on it: verify in Debug, skip publishing until a
hosting target is chosen. So `run-tests.ps1` now takes `-Configuration`, `phase-smoke.ps1` takes
`-FromSource`, and both print which mode they ran in, so a phase report cannot claim the stronger
form by accident (ASM-16). The 196 backend tests ran in Debug, clean.

What this costs, stated rather than absorbed: **D5 and D6 are not evidenced** for P07, and D3 and
D15 are evidenced in Debug rather than Release. The coverage figure is not comparable with P06's
for the same reason — Debug emits more sequence points — and the phase report says so instead of
reading the difference as a regression.

The blocker stays OPEN. It is not a defect in the code and it blocks no requirement; it blocks a
form of evidence. One line from Manish clears it: Windows Security > App & browser control > Smart
App Control, off or with `G:\software-management` excluded.
