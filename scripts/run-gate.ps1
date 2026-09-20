<#
.SYNOPSIS
  Runs the mechanical part of a phase exit gate (G0-G8) and writes its evidence.

.DESCRIPTION
  The protocol writes the gate out as a page of shell to be typed at each phase. Typed shell drifts:
  the P07 gate found that the `.editorconfig` guardrail hash had been stale since P03, so the one
  check whose job is to notice a loosened guardrail had been passing on a stale comparison for four
  phases, and that the secret scan's pathspec had been written three different ways across three
  phases. A gate that is retyped is a gate that is not the same gate twice.

  So the checks live here, run identically every phase, and read every phase-specific value from
  STATE.json (R-25). Nothing in this file decides whether a check is applicable: `dodApplicable` is
  frozen in the blueprint and this script asserts exactly what it lists.

  G9 (commit, secret scan, push proof), G10 (backup) and G11 (state, close) stay outside: they
  change the repository and are run deliberately, once, after this script reports PASS.

.PARAMETER RefreezeGuardrail
  A guardrail whose frozen hash is being deliberately re-recorded. Requires -RefreezeAdr. Both the
  old and the new hash are printed, so a re-freeze is visible in the transcript rather than silent.
  Re-freezing without an ADR is refused: that is the R-13 path this exists to keep closed.
#>
[CmdletBinding()]
param(
    [string]$Root = 'G:\software-management',
    [string[]]$RefreezeGuardrail = @(),
    [string]$RefreezeAdr,
    [switch]$SkipNpmCi
)

$ErrorActionPreference = 'Stop'
Set-Location $Root

if ($Root -match '^[A-Za-z]:\\?$' -or $Root -match '\s') { throw 'R-26: unsafe ROOT' }

$S = Get-Content "$Root\STATE.json" -Raw -Encoding UTF8 | ConvertFrom-Json
if (($S.rootPath -replace '/', '\') -ne $Root) { throw "R-26: STATE.json rootPath $($S.rootPath) != $Root" }

$PH = '{0:D2}' -f $S.currentPhase
$P = $S.phases | Where-Object { $_.id -eq $S.currentPhase }
$prevP = $S.phases | Where-Object { $_.id -eq ($S.currentPhase - 1) }
if (-not $P) { throw "G0: no phase $($S.currentPhase) in STATE.json" }

$EV = "$Root\_evidence\phase-$PH"
$nonce = [guid]::NewGuid().ToString('N').Substring(0, 8)
if ($prevP -and $nonce -eq $prevP.gate.nonce) { throw 'nonce reused across phases' }
New-Item -ItemType Directory -Force -Path $EV | Out-Null

$sln = Join-Path $Root $S.paths.sln
$apiCsproj = Join-Path $Root $S.paths.apiCsproj
$infra = Join-Path $Root $S.paths.infra
$fe = Join-Path $Root $S.paths.frontend
$distDir = Join-Path $Root $S.paths.distDir
$gateDb = "$($S.paths.dbName)_Gate"

# The repo-local Node toolchain, so the gate never depends on what happens to be on PATH.
$nodeDir = Join-Path $Root 'tools\node\node-v24.20.0-win-x64'
if (Test-Path $nodeDir) { $env:PATH = "$nodeDir;$env:PATH" }

$failures = New-Object System.Collections.Generic.List[string]

function Write-Banner {
    param([string]$Label)
    Write-Output ''
    Write-Output "===== [$Label] PHASE=$PH NONCE=$nonce AT=$(Get-Date -Format o) ====="
}

function Invoke-Gate {
    <#
        The only way a native command runs here. PowerShell 5.1 turns a native command's stderr into
        ErrorRecords when it is piped, and with $ErrorActionPreference='Stop' the first stderr line
        throws even on exit 0 - npm, ng and MSBuild all write to stderr routinely. Redirecting
        inside cmd.exe keeps the stream out of PowerShell entirely, and the exit code is read rather
        than inferred.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$CommandLine,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [string]$WorkDir = $Root,
        [int]$TimeoutMs = 900000,
        [switch]$AllowFailure
    )

    Write-Banner $Label
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $process = Start-Process -FilePath 'cmd.exe' `
        -ArgumentList "/c $CommandLine > `"$LogPath`" 2>&1" `
        -WorkingDirectory $WorkDir -PassThru -WindowStyle Hidden

    if (-not $process.WaitForExit($TimeoutMs)) {
        try { Stop-Process -Id $process.Id -Force } catch { }
        Get-Content $LogPath -Tail 40
        $ErrorActionPreference = $previous
        throw "GATE FAIL $Label : HUNG > $($TimeoutMs / 60000) min - a hang is a failure, not progress."
    }

    $code = $process.ExitCode
    $ErrorActionPreference = $previous
    Get-Content $LogPath -Tail 25
    Write-Output "EXITCODE=$code"
    if ($code -ne 0 -and -not $AllowFailure) { throw "GATE FAIL $Label : exit $code - see $LogPath" }
    # Deliberately returns nothing. A function that returns its exit code invites the caller to
    # write `Invoke-Gate ... | Out-Null`, and in PowerShell that pipes the whole success stream to
    # Out-Null, not just the return value: the banner, the log tail and the EXITCODE line all
    # vanish from the transcript. A gate whose evidence is discarded is not a gate.
}

function Invoke-Sql {
    param([string]$Database = 'master', [Parameter(Mandatory = $true)][string]$Query)
    $cs = "Server=$($S.environment.dbInstance);Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $Query; $cmd.CommandTimeout = 180
        $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        $dt = New-Object System.Data.DataTable
        [void]$da.Fill($dt)
        # Returned inside an array: PowerShell unrolls a DataTable on the way out of a function,
        # so a bare `return $dt` hands back DataRows and every `.Rows` on the far side is null.
        return , $dt
    }
    finally { $cn.Close() }
}

function Test-Dod { param([string]$Id) return ($P.dodApplicable -contains $Id) }

function Save-State {
    <#
        Rule S5. Windows PowerShell corrupts this file two ways if left to its defaults:
        ConvertTo-Json stops at depth 2 and silently stringifies the gate object, and Set-Content
        writes ANSI. Both produce a STATE.json that parses as something other than what was meant,
        which is worse than not writing it at all, so the result is read back and asserted.
    #>
    param([Parameter(Mandatory = $true)]$State)

    $State.lastUpdatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $json = $State | ConvertTo-Json -Depth 30
    Copy-Item "$Root\STATE.json" "$Root\STATE.prev.json" -Force -ErrorAction SilentlyContinue
    [System.IO.File]::WriteAllText("$Root\STATE.json.tmp", $json, (New-Object System.Text.UTF8Encoding($false)))
    $rt = Get-Content "$Root\STATE.json.tmp" -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($rt.phases.Count -lt 1) { throw 'STATE write corrupt: no phases' }
    if (-not $rt.phases[-1].gate) { throw 'STATE write corrupt: gate flattened (depth too small)' }
    if ((Get-Content "$Root\STATE.json.tmp" -Raw) -match 'System\.(Object|Collections)') { throw 'STATE write corrupt: object stringified' }
    Move-Item "$Root\STATE.json.tmp" "$Root\STATE.json" -Force
    Write-Output ('STATE_WRITE_OK sha256=' + (Get-FileHash "$Root\STATE.json" -Algorithm SHA256).Hash)
}

Write-Output "GATE-NONCE=$nonce PHASE=$PH AT=$(Get-Date -Format o)"
Write-Output "PHASE_NAME=$($P.name)"
Write-Output "REQ_IDS=$($P.reqIds -join ', ')"
Write-Output "DOD_APPLICABLE=$($P.dodApplicable -join ', ')"
Write-Output "MIN_TESTS=$($P.minTests) MIN_FE=$($P.minFrontendTests) PREV_TOTAL=$(if ($prevP) { $prevP.gate.test.total } else { 0 })"

# ---------------------------------------------------------------------------------------------
# G0 - state, freeze, guardrails, resources
# ---------------------------------------------------------------------------------------------
Write-Banner 'G0 FREEZE'

$reqNow = (Get-FileHash "$Root\docs\blueprint\04-requirements.md" -Algorithm SHA256).Hash
Write-Output "REQ_SHA_FROZEN=$($S.requirementsSha256)"
Write-Output "REQ_SHA_NOW   =$reqNow"
if ($S.requirementsSha256 -ne $reqNow) { throw 'GATE FAIL R-13: the frozen requirements contract was modified.' }

$musts = (Select-String "$Root\docs\blueprint\04-requirements.md" -Pattern '^\|\s*REQ-' |
    Where-Object { $_.Line -match '\|\s*Must\s*\|' }).Count
Write-Output "MUST_COUNT=$musts FROZEN=$($S.mustCount)"
if ($musts -ne $S.mustCount) { throw 'GATE FAIL R-13: MoSCoW labels changed after approval.' }

$protoNow = (Get-FileHash "$Root\docs\PROTOCOL.md" -Algorithm SHA256).Hash
Write-Output "PROTOCOL_SHA=$protoNow"
if ($S.protocolSha256 -and $S.protocolSha256 -ne $protoNow) { throw 'GATE FAIL R-13: docs/PROTOCOL.md was modified.' }

if ($RefreezeGuardrail.Count -gt 0 -and [string]::IsNullOrWhiteSpace($RefreezeAdr)) {
    throw 'G0: -RefreezeGuardrail requires -RefreezeAdr. Re-freezing a guardrail without a written decision is the R-13 path this check exists to close.'
}

$refrozen = @()
foreach ($g in $S.guardrailHashes.PSObject.Properties.Name) {
    $path = Join-Path $Root $g
    if (-not (Test-Path $path)) { throw "GATE FAIL R-13: guardrail $g is missing." }
    $h = (Get-FileHash $path -Algorithm SHA256).Hash
    $frozen = $S.guardrailHashes.$g
    if ($h -eq $frozen) {
        Write-Output "GUARD $g = OK"
    }
    elseif ($RefreezeGuardrail -contains $g) {
        Write-Output "GUARD_REFROZEN $g ADR=$RefreezeAdr WAS=$frozen NOW=$h"
        $S.guardrailHashes.$g = $h
        $refrozen += $g
    }
    else {
        throw "GATE FAIL R-13: $g changed after freeze (frozen $frozen, now $h). Diff it, decide deliberately, write the ADR, then re-run with -RefreezeGuardrail '$g' -RefreezeAdr <id>."
    }
}

if ($refrozen.Count -gt 0) {
    # Persisted here rather than at the end: if a later step fails, the decision that was already
    # taken and written into the ADR must not be lost, and the next run must not re-report it as
    # unexplained drift.
    Save-State -State $S
    Write-Output "GUARDRAIL_REFREEZE_PERSISTED=$($refrozen -join ', ')"
}

Select-String -Path "$Root\frontend\angular.json" -Pattern 'maximumWarning|maximumError' |
    ForEach-Object { Write-Output ('BUDGET ' + $_.Line.Trim()) }

if ($env:ASPNETCORE_ENVIRONMENT -or $env:ConnectionStrings__Default) {
    throw 'G0: leaked env from an earlier run - it changes which config and which database every later command uses.'
}
Write-Output 'NO_LEAKED_ENV=OK'

$sqlOk = Invoke-Sql -Query "SELECT 'SQL-OK' AS s;"
Write-Output ("SQL=" + $sqlOk.Rows[0][0])

$avail = try { [int](Get-Counter '\Memory\Available MBytes' -ErrorAction Stop).CounterSamples[0].CookedValue }
         catch { [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB) }
Write-Output "AVAILABLE_MB=$avail"
if ($avail -lt 1500) {
    dotnet build-server shutdown | Out-Null
    Get-Process VBCSCompiler, MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 3
    $avail = [int](Get-Counter '\Memory\Available MBytes').CounterSamples[0].CookedValue
    Write-Output "AVAILABLE_MB_AFTER=$avail"
    if ($avail -lt 900) { throw "GATE FAIL G0: ${avail} MB available - a resource blocker, not a code defect." }
}
$heap = [Math]::Min(3072, [Math]::Max(1024, $avail - 900))
$env:NODE_OPTIONS = "--max-old-space-size=$heap"
$env:MSBUILDDISABLENODEREUSE = '1'
Write-Output "NODE_HEAP_MB=$heap"

Invoke-Gate -Label 'G0 TOOLRESTORE' -CommandLine 'dotnet tool restore' -LogPath "$EV\tool-restore.log"

# ---------------------------------------------------------------------------------------------
# G1 - identity-checked port reclaim. A leaked listener serves a green gate for a stale binary.
# ---------------------------------------------------------------------------------------------
Write-Banner 'G1 PORTS'
foreach ($prt in @($S.paths.apiPort, $S.paths.staticPort)) {
    foreach ($c in @(Get-NetTCPConnection -LocalPort $prt -State Listen -ErrorAction SilentlyContinue)) {
        $opid = $c.OwningProcess
        if ($opid -le 10) { throw "G1 BLOCKED: port $prt held by system PID $opid. Change the port in STATE.json; never kill a system process." }
        $proc = Get-Process -Id $opid -ErrorAction SilentlyContinue
        $cmdl = (Get-CimInstance Win32_Process -Filter "ProcessId=$opid" -ErrorAction SilentlyContinue).CommandLine
        $path = try { $proc.Path } catch { $null }
        Write-Output "PORT $prt HELD BY PID $opid NAME=$($proc.ProcessName)"
        $mine = ($proc.ProcessName -in @('dotnet', 'node')) -and
                (($path -like "$Root*") -or ($cmdl -like "*$Root*") -or ($S.spawnedPids -contains $opid))
        if (-not $mine) { throw "G1 BLOCKED: port $prt held by a process this run did not start ($($proc.ProcessName), PID $opid). I will not kill it." }
        Write-Output "KILLING OUR OWN PID $opid"
        Stop-Process -Id $opid -Force
    }
}
# A test host left behind by an interrupted run keeps a lock on the assemblies the build is about
# to overwrite, which surfaces as MSB3027 and reads like a build defect.
foreach ($stale in @(Get-CimInstance Win32_Process -Filter "Name='testhost.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*$Root*" })) {
    Write-Output "KILLING STALE TESTHOST $($stale.ProcessId)"
    Stop-Process -Id $stale.ProcessId -Force -ErrorAction SilentlyContinue
}
dotnet build-server shutdown | Out-Null
Start-Sleep 2
foreach ($prt in @($S.paths.apiPort, $S.paths.staticPort)) {
    $held = @(Get-NetTCPConnection -LocalPort $prt -State Listen -ErrorAction SilentlyContinue)
    if ($held.Count -gt 0) { throw "G1: port $prt still held after the sweep." }
}
Write-Output 'PORTS CLEAR'

# ---------------------------------------------------------------------------------------------
# G2 - restore (D1)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D1') {
    Invoke-Gate -Label 'G2 RESTORE' -CommandLine "dotnet restore `"$sln`"" -LogPath "$EV\restore.log"
    if ($SkipNpmCi) {
        Write-Output 'G2 NPMCI: skipped by request (node_modules already installed from the committed lockfile)'
    }
    else {
        Invoke-Gate -Label 'G2 NPMCI' -CommandLine 'npm ci' -LogPath "$EV\npmci.log" -WorkDir $fe -TimeoutMs 900000
    }
}
else { Write-Output 'D1: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G3 - build (D2) and format
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D2') {
    Invoke-Gate -Label 'G3 BUILD' -CommandLine "dotnet build `"$sln`" -c Release --no-restore -warnaserror -m:1 -nodeReuse:false" -LogPath "$EV\build.log"
    Select-String -Path "$EV\build.log" -Pattern 'Warning\(s\)|Error\(s\)|Build succeeded|Build FAILED' |
        ForEach-Object { Write-Output ('BUILD ' + $_.Line.Trim()) }
    Invoke-Gate -Label 'G3 FORMAT' -CommandLine "dotnet format `"$sln`" --verify-no-changes" -LogPath "$EV\format.log"
}
else { Write-Output 'D2: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G4 - tests (D3) and coverage (D15)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D3') {
    Write-Banner 'G4 TEST'
    & "$Root\scripts\run-tests.ps1" -ResultsDirectory $EV
    if ($LASTEXITCODE -ne 0) { throw "GATE FAIL D3: the test run exited $LASTEXITCODE" }

    $summaries = Select-String -Path "$EV\test.log" -Pattern 'Failed:\s*\d+.*Total:\s*\d+'
    $total = 0; $failed = 0; $skipped = 0
    foreach ($line in $summaries) {
        $total += [int][regex]::Match($line.Line, 'Total:\s*(\d+)').Groups[1].Value
        $failed += [int][regex]::Match($line.Line, 'Failed:\s*(\d+)').Groups[1].Value
        $skipped += [int][regex]::Match($line.Line, 'Skipped:\s*(\d+)').Groups[1].Value
        Write-Output ('SUMMARY ' + $line.Line.Trim())
    }

    $minTests = [int]$P.minTests
    $prevTotal = if ($prevP) { [int]$prevP.gate.test.total } else { 0 }
    Write-Output "TOTAL=$total SKIPPED=$skipped FAILED=$failed MIN=$minTests PREV=$prevTotal REQ_COUNT=$($P.reqIds.Count)"
    if ($P.reqIds.Count -gt 0 -and $minTests -lt (2 * $P.reqIds.Count)) {
        throw "GATE FAIL D3: minTests $minTests < 2x REQ count $($P.reqIds.Count)"
    }
    if ($failed -ne 0) { throw "GATE FAIL D3: $failed tests failed" }
    if ($skipped -ne 0) { throw "GATE FAIL D3: $skipped tests skipped - a skipped test is an untested requirement" }
    if ($total -lt $minTests) { throw "GATE FAIL D3: $total tests < minTests $minTests" }
    if ($total -lt ($prevTotal + $minTests)) { throw "GATE FAIL D3: $total tests, needed $($prevTotal + $minTests) (previous $prevTotal plus this phase's $minTests)" }

    foreach ($r in $P.reqIds) {
        $pattern = $r -replace '-', '_'
        $n = (Select-String -Path (Join-Path $Root $S.paths.testsDir) -Recurse -Pattern $pattern -ErrorAction SilentlyContinue).Count
        Write-Output "$r TESTS=$n"
        if ($n -lt 2) { throw "GATE FAIL D3: $r has $n tests, needs at least a happy path and a rejection path" }
    }
}
else { Write-Output 'D3: N/A-BY-PLAN' }

if (Test-Dod 'D15') {
    Write-Banner 'G4 COVERAGE'
    $prevCov = if ($prevP -and $prevP.gate.coverage.line) { [double]$prevP.gate.coverage.line } else { 0 }
    & node "$Root\scripts\coverage.mjs" $EV $S.nfrCoverageFloor $prevCov
    if ($LASTEXITCODE -ne 0) { throw "GATE FAIL D15: coverage below the floor - write tests, never lower the floor (R-13)" }
}
else { Write-Output 'D15: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G5 - database (D4)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D4') {
    Invoke-Gate -Label 'G5 EFLIST' -CommandLine "dotnet dotnet-ef migrations list --project `"$infra`" --startup-project `"$apiCsproj`" --no-build --configuration Release" -LogPath "$EV\ef-list.log"

    $env:ConnectionStrings__Default = "Server=$($S.environment.dbInstance);Database=$gateDb;Trusted_Connection=True;TrustServerCertificate=True"
    try {
        Invoke-Gate -Label 'G5 EFUPDATE' -CommandLine "dotnet dotnet-ef database update --project `"$infra`" --startup-project `"$apiCsproj`" --no-build --configuration Release" -LogPath "$EV\ef.log"
        Invoke-Gate -Label 'G5 DRIFT' -CommandLine "dotnet dotnet-ef migrations has-pending-model-changes --project `"$infra`" --startup-project `"$apiCsproj`" --no-build --configuration Release" -LogPath "$EV\ef-drift.log"
    }
    finally { Remove-Item Env:ConnectionStrings__Default -ErrorAction SilentlyContinue }

    $applied = Invoke-Sql -Database $gateDb -Query 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;'
    Write-Output "MIGRATIONS_APPLIED=$($applied.Rows.Count)"
    $applied.Rows | ForEach-Object { Write-Output ('  ' + $_[0]) }

    $tables = Invoke-Sql -Database $gateDb -Query 'SELECT name FROM sys.tables ORDER BY name;'
    $tableNames = @($tables.Rows | ForEach-Object { $_[0] })
    Write-Output "TABLE_COUNT=$($tableNames.Count)"
    foreach ($t in $P.dbObjects) {
        $present = $tableNames -contains $t
        Write-Output "DB_OBJECT $t = $(if ($present) { 'PRESENT' } else { 'MISSING' })"
        if (-not $present) { throw "GATE FAIL D4: the phase declares table $t and it is not in the database" }
    }
}
else { Write-Output 'D4: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G6 - publish and API smoke (D5-D9)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D5') {
    Invoke-Gate -Label 'G6 PUBLISH' -CommandLine "dotnet publish `"$apiCsproj`" -c Release --no-build -o `"$Root\_publish\api`"" -LogPath "$EV\publish.log"
    $dll = Join-Path $Root $S.paths.apiDll
    if (-not (Test-Path $dll)) { throw 'GATE FAIL D5: the published dll is not on disk' }
    Write-Output ("PUBLISHED_DLL=$dll BYTES=" + (Get-Item $dll).Length)
}
else { Write-Output 'D5: N/A-BY-PLAN' }

if (Test-Dod 'D6') {
    Write-Banner 'G6 SMOKE'
    & "$Root\scripts\phase-smoke.ps1" -Nonce $nonce -Database $gateDb -Port $S.paths.apiPort -EvidenceDirectory $EV
    if ($LASTEXITCODE -ne 0) { throw "GATE FAIL D6-D9: the API smoke exited $LASTEXITCODE" }
}
else { Write-Output 'D6-D9: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G7 - frontend production build, bundle ceilings, render proof, tests, lint (D10)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D10') {
    Invoke-Gate -Label 'G7 NGBUILD' -CommandLine 'npx --no-install ng build --configuration production' -LogPath "$EV\ng-build.log" -WorkDir $fe -TimeoutMs 900000
    if (-not (Test-Path $distDir)) { throw "GATE FAIL D10: $distDir was not produced" }
    $distFiles = Get-ChildItem $distDir -Recurse -File | Measure-Object -Property Length -Sum
    Write-Output "DIST_FILES=$($distFiles.Count) DIST_BYTES=$($distFiles.Sum)"
    Write-Output ('DIST_JS_CHUNKS=' + (Get-ChildItem $distDir -Recurse -Filter *.js | Measure-Object).Count)

    Write-Banner 'G7 BUNDLES'
    & node "$Root\scripts\check-bundles.mjs" $distDir $S.bundleBudgetsKb.initial $S.bundleBudgetsKb.lazy
    if ($LASTEXITCODE -ne 0) { throw 'GATE FAIL D10: a bundle is over its approved ceiling. The ceiling is an NFR, not a knob (R-13).' }

    # The screens this phase promised must actually be compiled in.
    $bundle = (Get-ChildItem $distDir -Recurse -Filter *.js | Get-Content -Raw) -join "`n"
    foreach ($route in $P.phaseRoutes) {
        if ($bundle -notmatch [regex]::Escape($route)) { throw "GATE FAIL D10: route '$route' is not in the built bundle - the screen does not exist" }
        Write-Output "ROUTE_IN_BUNDLE=$route"
    }
    foreach ($sel in $P.phaseSelectors) {
        if ($bundle -notmatch [regex]::Escape($sel)) { throw "GATE FAIL D10: component <$sel> is not in the built bundle" }
        Write-Output "COMPONENT_IN_BUNDLE=$sel"
    }

    Write-Banner 'G7 RENDER'
    $markers = if ($P.renderMarkers) { $P.renderMarkers } else { @() }
    & "$Root\scripts\render-proof.ps1" -Routes $P.phaseRoutes -Markers $markers -Database $gateDb -Nonce $nonce -ApiPort $S.paths.apiPort -SsrPort $S.paths.staticPort -EvidenceDirectory $EV
    if ($LASTEXITCODE -ne 0) { throw "GATE FAIL D10: the server-render proof exited $LASTEXITCODE" }

    Invoke-Gate -Label 'G7 NGTEST' -CommandLine 'npm test' -LogPath "$EV\ng-test.log" -WorkDir $fe -TimeoutMs 900000
    $feLine = (Select-String -Path "$EV\ng-test.log" -Pattern 'Tests\s+\d+\s+passed' | Select-Object -Last 1)
    if (-not $feLine) { throw 'GATE FAIL D10: the frontend test run printed no summary' }
    $feTotal = [int][regex]::Match($feLine.Line, 'Tests\s+(\d+)\s+passed').Groups[1].Value
    $prevFe = if ($prevP -and $prevP.gate.frontendTests) { [int]$prevP.gate.frontendTests } else { 0 }
    Write-Output "FE_TESTS=$feTotal MIN_FE=$($P.minFrontendTests) PREV_FE=$prevFe"
    if ($feTotal -lt ($prevFe + [int]$P.minFrontendTests)) {
        throw "GATE FAIL D10: $feTotal frontend tests, needed $($prevFe + [int]$P.minFrontendTests)"
    }
    if ((Get-Content "$EV\ng-test.log" -Raw) -match 'Would you like to add') { throw 'R-21: a frontend command went interactive' }

    Invoke-Gate -Label 'G7 NGLINT' -CommandLine 'npx --no-install ng lint --format stylish' -LogPath "$EV\ng-lint.log" -WorkDir $fe -TimeoutMs 600000
}
else { Write-Output 'D10: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
# G8 - anti-stub scan (D11)
# ---------------------------------------------------------------------------------------------
if (Test-Dod 'D11') {
    Write-Banner 'G8 ANTISTUB'
    & node "$Root\scripts\anti-stub-scan.mjs"
    if ($LASTEXITCODE -ne 0) { throw 'GATE FAIL D11: the anti-stub scan found hits. Fix the code, never the detector (R-13).' }
}
else { Write-Output 'D11: N/A-BY-PLAN' }

# ---------------------------------------------------------------------------------------------
Write-Banner 'GATE G0-G8 COMPLETE'
Write-Output "NONCE=$nonce"
Write-Output "EVIDENCE=$EV"
if ($refrozen.Count -gt 0) { Write-Output "GUARDRAILS_REFROZEN=$($refrozen -join ', ') ADR=$RefreezeAdr" }
Write-Output 'RESULT=PASS (G9 commit+push, G10 backup, G11 close still to run)'
$nonce | Set-Content "$EV\nonce.txt" -Encoding utf8
if ($refrozen.Count -gt 0) {
    ($refrozen -join ',') + "|$RefreezeAdr" | Set-Content "$EV\refrozen.txt" -Encoding utf8
}
