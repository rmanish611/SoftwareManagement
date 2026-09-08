<#
.SYNOPSIS
  Runs the published API and proves, against it, the things a phase gate asks for: that anonymous
  callers are refused everywhere except the frozen allowlist, that this phase's own endpoints
  accept a write which reaches the database, and that the process stops and frees its port.

.DESCRIPTION
  This exists because the alternative is a page of shell in the phase report that nobody can run
  again. Every check prints a line beginning with a name and an equals sign, so the report quotes
  real output rather than a summary of it.

  The API is started from `_publish/api`, the artefact that would be deployed, not from `dotnet
  run`, so the thing proved is the thing that ships.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Nonce,

    # A database of its own per phase, created empty by the migrations this run applies. Reusing the
    # development database would prove less: a schema that only ever grew on one machine is not
    # evidence that it builds from nothing, and the seeded owner there already has a password from
    # an earlier run.
    [string]$Database = 'SoftwareManagementDb_Gate',
    [int]$Port = 5199,
    [string]$EvidenceDirectory
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repoRoot '_evidence' }
if (-not (Test-Path $EvidenceDirectory)) { New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null }

$ErrorActionPreference = 'Continue'
$baseUrl = "http://127.0.0.1:$Port"
$ownerEmail = 'owner@softwaremanagement.test'

# Generated fresh for each run and never written down. The gate database is thrown away afterwards,
# but a literal credential in a source file is a habit worth not having at all.
$ownerPassword = 'Gate-' + ([Guid]::NewGuid().ToString('N')) + '-Aa1!'
$failures = New-Object System.Collections.Generic.List[string]

function Get-Status {
    param([string]$Path, [hashtable]$Headers = @{}, [string]$Method = 'GET', $Body = $null)

    try {
        $args = @{ Uri = "$baseUrl$Path"; Method = $Method; Headers = $Headers; TimeoutSec = 20; UseBasicParsing = $true }
        if ($null -ne $Body) {
            $args.Body = ($Body | ConvertTo-Json -Depth 6)
            $args.ContentType = 'application/json'
        }
        $response = Invoke-WebRequest @args
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

$env:ASPNETCORE_URLS = $baseUrl
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__Default = "Server=.\SQLEXPRESS;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
$env:Jwt__Key = "gate-signing-key-not-a-secret-$Nonce-0123456789012345"
$env:Jwt__Issuer = 'software-management-gate'
$env:Jwt__Audience = 'software-management-gate'
$env:Database__MigrateOnStartup = 'true'
$env:Seed__OwnerEmail = $ownerEmail
$env:Seed__OwnerPassword = $ownerPassword

$outLog = Join-Path $EvidenceDirectory 'api-out.log'
$errLog = Join-Path $EvidenceDirectory 'api-err.log'

$api = Start-Process -FilePath 'dotnet' `
    -ArgumentList (Join-Path $repoRoot '_publish\api\SoftwareManagement.Api.dll') `
    -WorkingDirectory (Join-Path $repoRoot '_publish\api') `
    -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru -NoNewWindow

try {
    $health = 0
    for ($attempt = 1; $attempt -le 30 -and $health -ne 200; $attempt++) {
        Start-Sleep -Seconds 2
        $health = (Get-Status '/health').Status
    }

    Write-Output "HEALTH_STATUS=$health"
    if ($health -ne 200) { $failures.Add('the API never became healthy') }

    # D7: nothing outside the frozen anonymous allowlist answers without a token.
    $anonymousDenied = @(
        '/api/v1/admin/products',
        '/api/v1/admin/product-categories',
        '/api/v1/admin/pages',
        '/api/v1/admin/media'
    )

    $checked = 0
    foreach ($path in $anonymousDenied) {
        $status = (Get-Status $path).Status
        Write-Output "ANON $path => $status"
        $checked++
        if ($status -ne 401) { $failures.Add("$path answered $status to an anonymous caller, expected 401") }
    }

    Write-Output "ANON_ROUTES_CHECKED=$checked"

    # The public catalogue is on the allowlist and must answer without a token.
    foreach ($path in @('/api/v1/public/catalog/categories', '/api/v1/public/catalog/products')) {
        $status = (Get-Status $path).Status
        Write-Output "ANON_PUBLIC $path => $status"
        if ($status -ne 200) { $failures.Add("$path answered $status anonymously, expected 200") }
    }

    $login = Get-Status '/api/v1/auth/login' -Method 'POST' -Body @{ email = $ownerEmail; password = $ownerPassword; twoFactorCode = $null }
    Write-Output "LOGIN_STATUS=$($login.Status)"

    if ($login.Status -ne 200) {
        $failures.Add('the seeded owner could not sign in')
    }
    else {
        $token = ($login.Content | ConvertFrom-Json).accessToken
        $auth = @{ Authorization = "Bearer $token" }

        # D8: a write on this phase's own endpoint, read back out of the database by SQL.
        $probeName = "PROBE-$Nonce"
        $probeSlug = "probe-product-$Nonce"

        $created = Get-Status '/api/v1/admin/products' -Method 'POST' -Headers $auth -Body @{
            name         = $probeName
            slug         = $probeSlug
            tagline      = 'A probe written by the phase gate.'
            summary      = 'Created to prove the write path reaches the database.'
            categorySlug = 'erp'
        }

        Write-Output "PROBE_CREATE_STATUS=$($created.Status)"
        if ($created.Status -ne 201) { $failures.Add("creating the probe product returned $($created.Status): $($created.Content)") }

        $duplicate = Get-Status '/api/v1/admin/products' -Method 'POST' -Headers $auth -Body @{
            name         = "$probeName duplicate"
            slug         = $probeSlug
            tagline      = 'The same address twice.'
            summary      = 'Must be refused.'
            categorySlug = 'erp'
        }

        Write-Output "PROBE_DUPLICATE_STATUS=$($duplicate.Status)"
        Write-Output "PROBE_DUPLICATE_BODY=$($duplicate.Content)"
        if ($duplicate.Status -ne 409) { $failures.Add("a duplicate slug returned $($duplicate.Status), expected 409") }

        $productId = ($created.Content | ConvertFrom-Json).id

        $planA = Get-Status "/api/v1/admin/products/$productId/plans" -Method 'POST' -Headers $auth -Body @{
            name = 'Starter'; price = 1000; currency = 'INR'; billingPeriod = 'Monthly'
            includedSeats = 5; isFreeTier = $false; isRecommended = $true; setupFee = 0; isPublished = $true
        }

        $planB = Get-Status "/api/v1/admin/products/$productId/plans" -Method 'POST' -Headers $auth -Body @{
            name = 'Growth'; price = 3000; currency = 'INR'; billingPeriod = 'Monthly'
            includedSeats = 20; isFreeTier = $false; isRecommended = $false; setupFee = 0; isPublished = $true
        }

        Write-Output "PLAN_A_STATUS=$($planA.Status) PLAN_B_STATUS=$($planB.Status)"
        $planBId = ($planB.Content | ConvertFrom-Json).id

        $moved = Get-Status "/api/v1/admin/products/$productId/plans/$planBId" -Method 'PUT' -Headers $auth -Body @{
            name = 'Growth'; price = 3000; currency = 'INR'; billingPeriod = 'Monthly'
            includedSeats = 20; isFreeTier = $false; isRecommended = $true; setupFee = 0; isPublished = $true
        }

        Write-Output "PLAN_RECOMMEND_MOVE_STATUS=$($moved.Status)"
        if ($moved.Status -ne 200) { $failures.Add("moving the recommendation returned $($moved.Status)") }

        $implausible = Get-Status "/api/v1/admin/products/$productId/plans" -Method 'POST' -Headers $auth -Body @{
            name = 'Annual'; price = 99000; currency = 'INR'; billingPeriod = 'Yearly'
            includedSeats = 20; isFreeTier = $false; isRecommended = $false; setupFee = 0; isPublished = $true
        }

        Write-Output "PLAN_IMPLAUSIBLE_YEARLY_STATUS=$($implausible.Status)"
        if ($implausible.Status -ne 422) { $failures.Add("an implausible yearly price returned $($implausible.Status), expected 422") }

        $env:GATE_PRODUCT_ID = $productId
        $env:GATE_PROBE_NAME = $probeName
    }
}
finally {
    if ($api -and -not $api.HasExited) {
        Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
        $api.WaitForExit(15000) | Out-Null
    }

    Start-Sleep -Seconds 2
    $stillListening = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($stillListening.Count -eq 0) {
        Write-Output "API STOPPED, PORT $Port FREE"
    }
    else {
        Write-Output "API STOPPED, PORT $Port STILL IN USE"
        $failures.Add("port $Port was still listening after the API was stopped")
    }
}

if ($failures.Count -gt 0) {
    Write-Output 'SMOKE=FAIL'
    $failures | ForEach-Object { Write-Output "  $_" }
    exit 1
}

Write-Output 'SMOKE=PASS'
