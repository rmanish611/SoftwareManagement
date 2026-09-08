<#
.SYNOPSIS
  Restores the project-local Node.js toolchain into tools/node.

.DESCRIPTION
  Angular 22 requires Node ^22.22.3, ^24.15.0 or >=26. This machine's system Node is older,
  and upgrading it would change every other project on the machine, so the frontend toolchain
  is kept inside the repository instead. Nothing outside tools/node is touched and nothing is
  installed system-wide.

  The archive is verified against the official SHASUMS256.txt before it is extracted; a
  checksum mismatch aborts without writing anything.

  tools/node is gitignored. Run this script once on a fresh clone, or let the frontend gate
  invoke it. CI does not need it: the workflow uses actions/setup-node pinned to the same
  version.

.EXAMPLE
  pwsh -File tools/get-node.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = (Get-Content (Join-Path $PSScriptRoot '..' '.nvmrc') -Raw).Trim()
)

$ErrorActionPreference = 'Stop'

$archName = "node-v$Version-win-x64"
$targetRoot = Join-Path $PSScriptRoot 'node'
$targetDir = Join-Path $targetRoot $archName

if (Test-Path (Join-Path $targetDir 'node.exe')) {
    $installed = & (Join-Path $targetDir 'node.exe') --version
    Write-Host "Node $installed already present at $targetDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
$zipPath = Join-Path $targetRoot "$archName.zip"
$downloadUrl = "https://nodejs.org/dist/v$Version/$archName.zip"

Write-Host "Downloading $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing

Write-Host 'Verifying the official checksum'
$sums = (Invoke-WebRequest -Uri "https://nodejs.org/dist/v$Version/SHASUMS256.txt" -UseBasicParsing).Content
$expectedLine = ($sums -split "`n") | Where-Object { $_ -match [regex]::Escape("$archName.zip") } | Select-Object -First 1
if (-not $expectedLine) { throw "No checksum published for $archName.zip" }
$expected = ($expectedLine -split '\s+')[0].ToLowerInvariant()
$actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

if ($expected -ne $actual) {
    Remove-Item $zipPath -Force
    throw "Checksum mismatch for $archName.zip. Expected $expected, got $actual. Nothing was extracted."
}

Write-Host "Checksum verified: $actual"
Expand-Archive -Path $zipPath -DestinationPath $targetRoot -Force
Remove-Item $zipPath -Force

$installed = & (Join-Path $targetDir 'node.exe') --version
Write-Host "Node $installed ready at $targetDir"
