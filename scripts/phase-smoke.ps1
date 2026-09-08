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

function Measure-JsonArray {
    <#
        Counts the items in a JSON array response.

        Written out rather than inlined because `@('[]' | ConvertFrom-Json).Count` is 1 in Windows
        PowerShell: ConvertFrom-Json returns nothing for an empty array, and @() wraps that nothing
        in a one-element array holding $null. A check that reports 1 for an empty list is a check
        that cannot fail, which is worse than no check.
    #>
    param([string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) { return 0 }

    $parsed = $Json | ConvertFrom-Json
    if ($null -eq $parsed) { return 0 }
    if ($parsed -is [array]) { return $parsed.Count }
    return 1
}

function Send-PngUpload {
    <#
        Uploads a one-pixel PNG to the media library.

        The multipart body is assembled by hand because Windows PowerShell's Invoke-WebRequest has
        no -Form parameter. The bytes are a real PNG, not a renamed text file: the API inspects the
        first bytes of every upload and refuses anything whose content does not match what it claims
        to be, so a fake would be rejected here exactly as it should be.
    #>
    param([string]$Token, [string]$BaseUrl, [string]$Nonce)

    $png = [byte[]]@(
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    )

    $boundary = "----smoke$Nonce"
    $newline = "`r`n"
    $encoding = [System.Text.Encoding]::GetEncoding('iso-8859-1')

    $head = "--$boundary$newline" +
        "Content-Disposition: form-data; name=`"file`"; filename=`"probe-$Nonce.png`"$newline" +
        "Content-Type: image/png$newline$newline"

    $tail = "$newline--$boundary$newline" +
        "Content-Disposition: form-data; name=`"altText`"$newline$newline" +
        "The probe screenshot$newline" +
        "--$boundary--$newline"

    # Assembled through a MemoryStream rather than by adding arrays: PowerShell's + on byte arrays
    # produces an Object[], which Invoke-WebRequest then sends as text and the server reads as a
    # truncated form.
    $stream = New-Object System.IO.MemoryStream
    $headBytes = $encoding.GetBytes($head)
    $tailBytes = $encoding.GetBytes($tail)
    $stream.Write($headBytes, 0, $headBytes.Length)
    $stream.Write($png, 0, $png.Length)
    $stream.Write($tailBytes, 0, $tailBytes.Length)
    $body = $stream.ToArray()
    $stream.Dispose()

    try {
        $request = [System.Net.HttpWebRequest]::Create("$BaseUrl/api/v1/admin/media")
        $request.Method = 'POST'
        $request.ContentType = "multipart/form-data; boundary=$boundary"
        $request.Headers.Add('Authorization', "Bearer $Token")
        $request.ContentLength = $body.Length
        $request.Timeout = 30000

        $requestStream = $request.GetRequestStream()
        $requestStream.Write($body, 0, $body.Length)
        $requestStream.Close()

        $response = $request.GetResponse()
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        $content = $reader.ReadToEnd()
        $status = [int]$response.StatusCode
        $response.Close()

        return [pscustomobject]@{ Status = $status; Content = $content }
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

        # The publishing gate. The probe has a plan and no features or screenshots, so it is exactly
        # the case BR-CAT-01 exists to refuse, and the response has to name the failing counts.
        $publish = Get-Status "/api/v1/admin/products/$productId/publish" -Method 'POST' -Headers $auth
        Write-Output "PUBLISH_INCOMPLETE_STATUS=$($publish.Status)"
        Write-Output "PUBLISH_INCOMPLETE_BODY=$($publish.Content)"
        if ($publish.Status -ne 422) { $failures.Add("publishing an incomplete product returned $($publish.Status), expected 422") }
        if ($publish.Content -notlike '*features*') { $failures.Add('the publish refusal did not name the failing counts') }

        # A draft's public address is a 404 to a stranger, never a 403 (NFR-AUTHZ-04).
        $draft = Get-Status "/api/v1/public/products/$probeSlug"
        Write-Output "PUBLIC_DRAFT_STATUS=$($draft.Status)"
        if ($draft.Status -ne 404) { $failures.Add("a draft product's public address answered $($draft.Status), expected 404") }

        $readiness = Get-Status "/api/v1/admin/products/$productId/readiness" -Headers $auth
        Write-Output "READINESS_STATUS=$($readiness.Status)"
        Write-Output "READINESS_BODY=$($readiness.Content)"
        if ($readiness.Status -ne 200) { $failures.Add("the readiness report returned $($readiness.Status)") }

        # Bring the probe up to the thresholds and publish it for real, so the public catalogue has
        # something in it and the whole path is exercised: upload, attach, describe, publish, read.
        foreach ($feature in @('Ledger', 'Invoicing', 'Reporting')) {
            $added = Get-Status "/api/v1/admin/products/$productId/features" -Method 'POST' -Headers $auth -Body @{
                name = $feature; description = "What $feature does."; groupName = 'Core'; isHighlighted = $false
            }
            if ($added.Status -ne 200) { $failures.Add("adding the feature $feature returned $($added.Status)") }
        }

        $upload = Send-PngUpload -Token $token -BaseUrl $baseUrl -Nonce $Nonce
        Write-Output "MEDIA_UPLOAD_STATUS=$($upload.Status)"
        if ($upload.Status -ne 201 -and $upload.Status -ne 200) {
            $failures.Add("uploading the probe screenshot returned $($upload.Status): $($upload.Content)")
        }
        else {
            $assetId = ($upload.Content | ConvertFrom-Json).id
            $attached = Get-Status "/api/v1/admin/products/$productId/screenshots" -Method 'POST' -Headers $auth -Body @{
                mediaAssetId = $assetId; caption = 'The dashboard'
            }
            Write-Output "SCREENSHOT_ATTACH_STATUS=$($attached.Status)"
            if ($attached.Status -ne 200) { $failures.Add("attaching the screenshot returned $($attached.Status)") }
        }

        $publishNow = Get-Status "/api/v1/admin/products/$productId/publish" -Method 'POST' -Headers $auth
        Write-Output "PUBLISH_COMPLETE_STATUS=$($publishNow.Status)"
        if ($publishNow.Status -ne 204) { $failures.Add("publishing the completed product returned $($publishNow.Status): $($publishNow.Content)") }

        $publicPage = Get-Status "/api/v1/public/products/$probeSlug"
        Write-Output "PUBLIC_PAGE_STATUS=$($publicPage.Status)"
        if ($publicPage.Status -ne 200) { $failures.Add("the published product's public page answered $($publicPage.Status)") }
        if ($publicPage.Content -notlike '*Ledger*') { $failures.Add('the public product page did not carry its feature list') }

        # A caller asking for a hundred thousand gets the ceiling, not a slow query (NFR-PERF-04).
        $huge = Get-Status '/api/v1/public/products?pageSize=100000'
        $returned = if ($huge.Status -eq 200) { Measure-JsonArray $huge.Content } else { -1 }
        Write-Output "PUBLIC_PAGESIZE_CAP_STATUS=$($huge.Status) ITEMS=$returned"
        if ($huge.Status -ne 200 -or $returned -gt 100 -or $returned -lt 1) {
            $failures.Add("pageSize=100000 returned $returned items with status $($huge.Status)")
        }

        $searched = Get-Status '/api/v1/public/products?search=Invoicing'
        $found = if ($searched.Status -eq 200) { Measure-JsonArray $searched.Content } else { -1 }
        Write-Output "PUBLIC_SEARCH_STATUS=$($searched.Status) ITEMS=$found"
        if ($found -lt 1) { $failures.Add('searching a word from a feature found nothing') }

        $noMatch = Get-Status "/api/v1/public/products?search=nothing-matches-$Nonce"
        $none = if ($noMatch.Status -eq 200) { Measure-JsonArray $noMatch.Content } else { -1 }
        Write-Output "PUBLIC_SEARCH_EMPTY_STATUS=$($noMatch.Status) ITEMS=$none"
        if ($noMatch.Status -ne 200 -or $none -ne 0) { $failures.Add("a search matching nothing returned $none items with status $($noMatch.Status)") }

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
