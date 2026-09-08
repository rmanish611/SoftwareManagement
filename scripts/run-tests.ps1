<#
.SYNOPSIS
  Runs the backend test suite and collects coverage, in the only way that works reliably on this
  machine.

.DESCRIPTION
  Two environment facts shape this script, both discovered by measurement rather than assumed:

  1. Windows Smart App Control is enforced here (VerifiedAndReputablePolicyState = 1) and refuses
     to load unsigned assemblies it has not evaluated. Coverlet's collector rewrites the assemblies
     on disk at run time, producing new unsigned files on every run, which Smart App Control blocks
     with Event 3077. dotnet-coverage attaches a profiler instead and writes nothing new, so it is
     used here. Nothing about coverage measurement is weakened: the same line data is produced in
     the same Cobertura format, and the floor is unchanged.

  2. Running the whole solution in one command starts four test hosts at once. On a machine with
     7.9 GB that is wasteful, and it multiplies the assembly-load contention above. Projects run
     one at a time.

  A genuine test failure is reported immediately and never retried. Only the Smart App Control
  block is retried, and the retry count is printed so every phase report can state it.

.PARAMETER ResultsDirectory
  Where the .trx files and the merged coverage report are written.
#>
[CmdletBinding()]
param(
    [string]$ResultsDirectory,
    [int]$MaxAttempts = 3,
    [int]$RetryDelaySeconds = 20
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $repoRoot '_evidence' }

$ErrorActionPreference = 'Continue'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$blockMarker = 'An Application Control policy has blocked this file'
$logPath = Join-Path $ResultsDirectory 'test.log'
$coveragePath = Join-Path $ResultsDirectory 'coverage.cobertura.xml'
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$projects = Get-ChildItem (Join-Path $repoRoot 'backend\tests') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } |
    Sort-Object Name

if (-not $projects) { Write-Host 'No test projects found.'; exit 1 }

'' | Out-File -FilePath $logPath -Encoding utf8
$overall = 0
$totalRetries = 0

foreach ($project in $projects) {
    $projectCoverage = Join-Path $ResultsDirectory "coverage-$($project.Name).cobertura.xml"

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        Write-Host "===== [TEST $($project.Name) attempt $attempt] ====="

        $command = "dotnet test `"$($project.FullName)`" -c Release --no-build " +
                   "--logger `"trx;LogFileName=$($project.Name).trx`" --results-directory `"$ResultsDirectory`""

        $output = & dotnet dotnet-coverage collect --output-format cobertura --output $projectCoverage $command 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        $text | Out-File -FilePath $logPath -Encoding utf8 -Append

        $summary = ($text -split "`n" | Select-String -Pattern 'Passed!|Failed!' | Select-Object -First 1)
        if ($summary) { Write-Host $summary.ToString().Trim() }

        if ($exitCode -eq 0) { break }

        if ($text -notlike "*$blockMarker*") {
            Write-Host "TEST RESULT: genuine failure in $($project.Name), not retried."
            $overall = $exitCode
            break
        }

        $blocks = ([regex]::Matches($text, [regex]::Escape($blockMarker))).Count
        Write-Host "Smart App Control blocked $blocks assembly loads; waiting $RetryDelaySeconds seconds."
        $totalRetries++
        Start-Sleep -Seconds $RetryDelaySeconds

        if ($attempt -eq $MaxAttempts) {
            Write-Host "TEST RESULT: Smart App Control kept blocking $($project.Name)."
            $overall = 1
        }
    }
}

# Merge the per-project reports into one file for the coverage gate.
$reports = Get-ChildItem $ResultsDirectory -Filter 'coverage-*.cobertura.xml' -ErrorAction SilentlyContinue
if ($reports) {
    & dotnet dotnet-coverage merge --output-format cobertura --output $coveragePath ($reports.FullName) | Out-Null
    Write-Host "COVERAGE_REPORT=$coveragePath"
}

Write-Host "SAC_RETRIES=$totalRetries"
Select-String -Path $logPath -Pattern 'Passed!|Failed!' | ForEach-Object { $_.Line.Trim() }
exit $overall
