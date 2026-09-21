<#
.SYNOPSIS
  Proves the site renders on the server, by running the two processes production runs: the published
  API and `dist/server/server.mjs`.

.DESCRIPTION
  A screenshot proves nothing that a static file server could not fake. This starts the real
  server-rendering process against the real API, asks for the routes the phase declared, and reports
  the status and a marker found in the HTML the server sent, before any browser JavaScript ran.

  Pass a marker that is CONTENT, not a test id. A `data-testid` on a section wrapper is present
  whether or not the page reached the API, so it proves the route rendered and nothing more. That
  exact mistake hid a real defect for a phase: every server-rendered page was an empty shell,
  because the renderer's own API calls were resolving back to the renderer. A marker that is a
  product name or a company name cannot be produced without the data.

  It also asks for a file that does not exist. A single-page application that answers 200 with HTML
  for a missing script is a real defect, so that check is here rather than in a comment.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Routes,
    [string[]]$Markers = @(),
    [string]$Database = 'SoftwareManagementDb_Gate',
    [string]$Nonce = 'render',
    [int]$ApiPort = 5199,
    [int]$SsrPort = 4300,
    [string]$EvidenceDirectory,

    # A route whose server-rendered HTML must carry parseable JSON-LD (NFR-SEO-04). Checked here
    # rather than in a component test because the claim is about what a crawler receives, and a
    # crawler does not run the component.
    [string]$JsonLdRoute,

    # Run the API from source instead of from _publish/api, for the same reason phase-smoke.ps1
    # has this switch: publishing is off for this project until the hosting target is chosen. It
    # changes what the run proves, and the run prints which mode it was in.
    [switch]$FromSource
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repoRoot '_evidence' }
if (-not (Test-Path $EvidenceDirectory)) { New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null }

$ErrorActionPreference = 'Continue'
$failures = New-Object System.Collections.Generic.List[string]

function Get-Page {
    param([string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec 30 -UseBasicParsing
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = $response.Content }
    }
    catch [System.Net.WebException] {
        $r = $_.Exception.Response
        if ($null -eq $r) { return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message } }
        $reader = New-Object System.IO.StreamReader($r.GetResponseStream())
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Content = $reader.ReadToEnd() }
    }
    catch {
        return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message }
    }
}

$env:ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__Default = "Server=.\SQLEXPRESS;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
$env:Jwt__Key = "render-proof-key-not-a-secret-$Nonce-012345678901234"
$env:Jwt__Issuer = 'software-management-gate'
$env:Jwt__Audience = 'software-management-gate'
$env:Database__MigrateOnStartup = 'true'

$apiDll = if ($FromSource) {
    Join-Path $repoRoot 'backend\src\SoftwareManagement.Api\bin\Debug\net10.0\SoftwareManagement.Api.dll'
}
else {
    Join-Path $repoRoot '_publish\api\SoftwareManagement.Api.dll'
}

Write-Output ("API_SOURCE=$apiDll" + $(if ($FromSource) { ' - NOT the published artefact' } else { '' }))

$api = Start-Process -FilePath 'dotnet' `
    -ArgumentList $apiDll `
    -WorkingDirectory (Split-Path -Parent $apiDll) `
    -RedirectStandardOutput (Join-Path $EvidenceDirectory 'render-api-out.log') `
    -RedirectStandardError (Join-Path $EvidenceDirectory 'render-api-err.log') `
    -PassThru -NoNewWindow

$nodeExe = Join-Path $repoRoot 'tools\node\node-v24.20.0-win-x64\node.exe'
$serverEntry = Join-Path $repoRoot 'frontend\dist\software-management\server\server.mjs'

$env:PORT = "$SsrPort"
$env:API_BASE_URL = "http://127.0.0.1:$ApiPort"
$env:ALLOWED_HOSTS = "127.0.0.1:$SsrPort,localhost:$SsrPort"

$ssr = $null

# Counted, and asserted at the end. A script that throws part-way through and then prints PASS is
# worse than no script at all, so the number of routes actually reached has to match the number
# asked for before this reports success.
$routesChecked = 0

try {
    $health = 0
    for ($attempt = 1; $attempt -le 30 -and $health -ne 200; $attempt++) {
        Start-Sleep -Seconds 2
        $health = (Get-Page "http://127.0.0.1:$ApiPort/health").Status
    }

    Write-Output "API_HEALTH=$health"
    if ($health -ne 200) { $failures.Add('the API never became healthy') }

    $ssr = Start-Process -FilePath $nodeExe -ArgumentList $serverEntry `
        -WorkingDirectory (Join-Path $repoRoot 'frontend\dist\software-management\server') `
        -RedirectStandardOutput (Join-Path $EvidenceDirectory 'ssr-out.log') `
        -RedirectStandardError (Join-Path $EvidenceDirectory 'ssr-err.log') `
        -PassThru -NoNewWindow

    $landing = [pscustomobject]@{ Status = 0 }
    for ($attempt = 1; $attempt -le 30 -and $landing.Status -ne 200; $attempt++) {
        Start-Sleep -Seconds 2
        $landing = Get-Page "http://127.0.0.1:$SsrPort/"
    }

    Write-Output "NG_INDEX_STATUS=$($landing.Status)"
    if ($landing.Status -ne 200) { $failures.Add("the server-rendering process never served the home page") }

    $appRoots = ([regex]::Matches($landing.Content, "<app-root")).Count
    Write-Output "NG_APP_ROOT_COUNT=$appRoots"
    if ($appRoots -ne 1) { $failures.Add("the home page contained $appRoots app-root elements, expected 1") }

    for ($index = 0; $index -lt $Routes.Count; $index++) {
        $route = $Routes[$index]
        $page = Get-Page "http://127.0.0.1:$SsrPort$route"
        $marker = if ($index -lt $Markers.Count) { $Markers[$index] } else { '' }
        $found = if ($marker -and $page.Content -like "*$marker*") { 'yes' } elseif ($marker) { 'no' } else { 'n/a' }

        Write-Output "ROUTE $route => $($page.Status) marker='$marker' found=$found"

        if ($page.Status -ne 200) { $failures.Add("$route answered $($page.Status)") }
        if ($marker -and $found -eq 'no') { $failures.Add("$route did not contain '$marker' in the server-rendered HTML") }
        $routesChecked++
    }

    if ($JsonLdRoute) {
        $structured = Get-Page "http://127.0.0.1:$SsrPort$JsonLdRoute"
        $blocks = [regex]::Matches(
            $structured.Content,
            '<script[^>]*type="application/ld\+json"[^>]*>(.*?)</script>',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)

        Write-Output "JSONLD_ROUTE=$JsonLdRoute BLOCKS=$($blocks.Count)"

        if ($blocks.Count -lt 1) {
            $failures.Add("$JsonLdRoute carried no JSON-LD in the server-rendered HTML")
        }
        else {
            foreach ($block in $blocks) {
                $raw = [System.Net.WebUtility]::HtmlDecode($block.Groups[1].Value)
                try {
                    $data = $raw | ConvertFrom-Json
                    $type = $data.'@type'
                    Write-Output "JSONLD_TYPE=$type HEADLINE=$($data.headline) PUBLISHED=$($data.datePublished)"

                    foreach ($field in @('@context', '@type', 'headline', 'datePublished')) {
                        if (-not $data.$field) { $failures.Add("the JSON-LD on $JsonLdRoute has no $field") }
                    }
                }
                catch {
                    Write-Output "JSONLD_PARSE=FAIL"
                    $failures.Add("the JSON-LD on $JsonLdRoute did not parse: $($_.Exception.Message)")
                }
            }
        }
    }

    # A missing file must be a 404, not an HTML page a browser would try to run as JavaScript.
    $missing = Get-Page "http://127.0.0.1:$SsrPort/does-not-exist-$Nonce.js"
    Write-Output "NG_MISSING_ASSET_STATUS=$($missing.Status)"
    if ($missing.Status -ne 404) { $failures.Add("a missing asset answered $($missing.Status), expected 404") }
}
finally {
    foreach ($process in @($ssr, $api)) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(15000) | Out-Null
        }
    }

    Start-Sleep -Seconds 2
    foreach ($port in @($SsrPort, $ApiPort)) {
        $listening = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
        if ($listening.Count -eq 0) {
            Write-Output "PORT $port FREE"
        }
        else {
            Write-Output "PORT $port STILL IN USE"
            $failures.Add("port $port was still listening after shutdown")
        }
    }
}

Write-Output "ROUTES_CHECKED=$routesChecked EXPECTED=$($Routes.Count)"
if ($routesChecked -ne $Routes.Count) {
    $failures.Add("only $routesChecked of $($Routes.Count) routes were reached; the run did not finish")
}

if ($failures.Count -gt 0) {
    Write-Output 'RENDER_PROOF=FAIL'
    $failures | ForEach-Object { Write-Output "  $_" }
    exit 1
}

Write-Output 'RENDER_PROOF=PASS'
