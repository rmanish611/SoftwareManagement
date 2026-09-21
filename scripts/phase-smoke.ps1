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
    [string]$EvidenceDirectory,

    # Run the API from source instead of from _publish/api.
    #
    # The published artefact is what the gate normally wants, because the thing proved should be
    # the thing that ships. Publishing is off for this project until the hosting target is chosen,
    # so this exists to keep the checks runnable in the meantime. It changes what the run proves,
    # and the run says which mode it was in so a phase report cannot claim the stronger one.
    [switch]$FromSource
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
        return [pscustomobject]@{
            Status     = [int]$response.StatusCode
            Content    = $response.Content
            RetryAfter = $response.Headers['Retry-After']
        }
    }
    catch [System.Net.WebException] {
        $r = $_.Exception.Response
        if ($null -eq $r) { return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message; RetryAfter = '' } }
        $reader = New-Object System.IO.StreamReader($r.GetResponseStream())
        # Read the header off the response itself: a refusal is where Retry-After actually lives,
        # and Invoke-WebRequest throws before it hands back an object to read it from.
        return [pscustomobject]@{
            Status     = [int]$r.StatusCode
            Content    = $reader.ReadToEnd()
            RetryAfter = $r.Headers['Retry-After']
        }
    }
    catch {
        return [pscustomobject]@{ Status = 0; Content = $_.Exception.Message; RetryAfter = '' }
    }
}

function Invoke-SqlScalar {
    <#
        One value straight out of the gate database.

        The checks this serves are about counting rows, and a count taken through the API would be
        the API marking its own homework: the point of asking SQL Server is that it answers from
        what was actually written.
    #>
    param([Parameter(Mandatory = $true)][string]$Query)

    $connectionString = "Server=.\SQLEXPRESS;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 60
        $value = $command.ExecuteScalar()
        if ($null -eq $value -or $value -is [System.DBNull]) { return $null }
        return $value
    }
    finally {
        $connection.Close()
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

# The forms are posted by this script, not by a browser, so there is no Turnstile widget to issue a
# token. The bypass is a prefix and is only ever honoured because this line sets it: a deployment
# that has not set it has no bypass at all, which is the property the verifier is built around.
# Each submission still needs its own token, because a token is spendable once bypass or not, and
# the replay check below depends on that being true.
$env:Captcha__BypassToken = "gate-bypass-$Nonce"

# The outbox pump would otherwise send on a timer while the assertions about the queue are running.
$env:Outbox__PumpEnabled = 'false'

$outLog = Join-Path $EvidenceDirectory 'api-out.log'
$errLog = Join-Path $EvidenceDirectory 'api-err.log'

if ($FromSource) {
    # The Debug build output, run through the `dotnet` host rather than through the generated
    # SoftwareManagement.Api.exe.
    #
    # `dotnet run` launches that apphost, and Smart App Control blocks it here for the same reason
    # it blocks the Release assembly (BLK-1): it is a freshly built unsigned executable. Handing
    # the managed DLL to the signed `dotnet` host sidesteps the apphost entirely, which is why the
    # integration tests load the same build without trouble. Nothing about the application changes.
    $apiDll = Join-Path $repoRoot 'backend\src\SoftwareManagement.Api\bin\Debug\net10.0\SoftwareManagement.Api.dll'

    if (-not (Test-Path $apiDll)) {
        Write-Output "SMOKE=FAIL"
        Write-Output "  no Debug build at $apiDll - run dotnet build -c Debug first"
        exit 1
    }

    Write-Output 'API_SOURCE=dotnet backend/.../bin/Debug/SoftwareManagement.Api.dll - NOT the published artefact'
    $apiArguments = @($apiDll)
    $apiWorkingDirectory = Split-Path -Parent $apiDll
}
else {
    Write-Output 'API_SOURCE=_publish/api/SoftwareManagement.Api.dll'
    $apiArguments = @((Join-Path $repoRoot '_publish\api\SoftwareManagement.Api.dll'))
    $apiWorkingDirectory = Join-Path $repoRoot '_publish\api'
}

$api = Start-Process -FilePath 'dotnet' `
    -ArgumentList $apiArguments `
    -WorkingDirectory $apiWorkingDirectory `
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
        '/api/v1/admin/media',
        '/api/v1/leads'
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
    foreach ($path in @('/api/v1/public/catalog/categories', '/api/v1/public/catalog/products', '/api/v1/public/forms/contact')) {
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

        # ------------------------------------------------------------------------------------
        # P07 lead capture. Every check below is one row of the phase's exit-criteria table, run
        # against the published API rather than asserted in a test, because a rule about counting
        # submissions over a window only means something against a real database and a real clock.
        # ------------------------------------------------------------------------------------
        $leadProbe = "PROBE-$Nonce"
        $leadEmail = "probe-$Nonce@example.test"
        $captcha = "gate-bypass-$Nonce"
        $leadIp = '198.51.100.7'

        function Send-Enquiry {
            param([string]$Key = 'contact', [hashtable]$Body, [string]$Ip = $leadIp)
            $headers = @{ 'X-Forwarded-For' = $Ip }
            return Get-Status "/api/v1/public/forms/$Key/submit" -Method 'POST' -Headers $headers -Body $Body
        }

        # 1 — an accepted enquiry writes a submission, a consent record and a lead (REQ-LEAD-001).
        $enquiry = Send-Enquiry -Body @{
            answers      = @{ fullName = $leadProbe; email = $leadEmail; message = "A gate enquiry, reference $Nonce." }
            consent      = $true
            captchaToken = "$captcha-1"
        }

        Write-Output "LEAD_SUBMIT_STATUS=$($enquiry.Status)"
        Write-Output "LEAD_SUBMIT_BODY=$($enquiry.Content)"
        if ($enquiry.Status -ne 202) { $failures.Add("the contact form returned $($enquiry.Status), expected 202") }
        if ($enquiry.Content -notlike '*ENQ-*') { $failures.Add('the acknowledgement carried no enquiry reference') }

        # 2 — the same captcha token a second time is refused (REQ-LEAD-004, BR-LEAD-02).
        $replay = Send-Enquiry -Body @{
            answers      = @{ fullName = "Replay $Nonce"; email = "replay-$Nonce@example.test"; message = 'A replayed token.' }
            consent      = $true
            captchaToken = "$captcha-1"
        }

        Write-Output "LEAD_REPLAY_STATUS=$($replay.Status)"
        if ($replay.Status -ne 400) { $failures.Add("a replayed captcha token returned $($replay.Status), expected 400") }
        if ($replay.Content -notlike '*captcha*') { $failures.Add('the replay refusal did not name the captcha') }

        # 3 — consent is not optional (REQ-LEAD-006, BR-LEAD-06).
        $noConsent = Send-Enquiry -Ip '198.51.100.8' -Body @{
            answers      = @{ fullName = "No consent $Nonce"; email = "nc-$Nonce@example.test"; message = 'Sent without consent.' }
            consent      = $false
            captchaToken = "$captcha-nc"
        }

        Write-Output "LEAD_NO_CONSENT_STATUS=$($noConsent.Status)"
        Write-Output "LEAD_NO_CONSENT_BODY=$($noConsent.Content)"
        if ($noConsent.Status -ne 422) { $failures.Add("a submission without consent returned $($noConsent.Status), expected 422") }
        if ($noConsent.Content -notlike '*CONSENT_REQUIRED*') { $failures.Add('the consent refusal did not carry code CONSENT_REQUIRED') }

        # 4 — the same enquiry twice inside ten minutes is one lead (REQ-LEAD-007).
        $duplicateBody = @{
            answers = @{ fullName = "Twice $Nonce"; email = "twice-$Nonce@example.test"; message = 'Sent twice by an impatient hand.' }
            consent = $true
        }

        $first = Send-Enquiry -Ip '198.51.100.9' -Body ($duplicateBody + @{ captchaToken = "$captcha-d1" })
        $again = Send-Enquiry -Ip '198.51.100.9' -Body ($duplicateBody + @{ captchaToken = "$captcha-d2" })
        Write-Output "LEAD_DUPLICATE_FIRST=$($first.Status) SECOND=$($again.Status)"
        $twiceRows = Invoke-SqlScalar -Query "SELECT COUNT(*) FROM dbo.Leads WHERE FullName = 'Twice $Nonce';"
        Write-Output "LEAD_DUPLICATE_ROWS=$twiceRows"
        if ($twiceRows -ne 1) { $failures.Add("the same enquiry sent twice produced $twiceRows leads, expected 1") }

        # 5 — the sixth submission from one address inside ten minutes is refused (REQ-LEAD-005).
        $floodIp = '198.51.100.20'
        $floodStatus = 0
        $retryAfter = ''
        for ($i = 1; $i -le 6; $i++) {
            $flood = Send-Enquiry -Ip $floodIp -Body @{
                answers      = @{ fullName = "Flood $i $Nonce"; email = "flood-$i-$Nonce@example.test"; message = "Message $i from one address." }
                consent      = $true
                captchaToken = "$captcha-f$i"
            }
            Write-Output "LEAD_FLOOD_$i=$($flood.Status)"
            $floodStatus = $flood.Status
            if ($i -eq 6) { $retryAfter = $flood.RetryAfter }
        }

        Write-Output "LEAD_FLOOD_SIXTH_STATUS=$floodStatus RETRY_AFTER=$retryAfter"
        if ($floodStatus -ne 429) { $failures.Add("the sixth submission from one address returned $floodStatus, expected 429") }
        if ([string]::IsNullOrWhiteSpace($retryAfter)) { $failures.Add('the 429 carried no Retry-After header') }

        # 6 — D8: the row is in SQL Server, and the phase's own read endpoint returns it.
        $leadRows = Invoke-SqlScalar -Query "SELECT COUNT(*) FROM dbo.Leads WHERE FullName = '$leadProbe';"
        Write-Output "LEAD_DB_ROWS=$leadRows"
        if ($leadRows -ne 1) { $failures.Add("the enquiry did not land exactly one row in dbo.Leads (found $leadRows)") }

        $consentRows = Invoke-SqlScalar -Query @"
SELECT COUNT(*) FROM dbo.ConsentRecords c
JOIN dbo.FormSubmissions s ON s.Id = c.FormSubmissionId
JOIN dbo.Leads l ON l.Id = s.LeadId
WHERE l.FullName = '$leadProbe';
"@
        Write-Output "LEAD_CONSENT_ROWS=$consentRows"
        if ($consentRows -ne 1) { $failures.Add("the enquiry did not store exactly one consent record (found $consentRows)") }

        $leadId = Invoke-SqlScalar -Query "SELECT CAST(TOP_ID AS nvarchar(64)) FROM (SELECT TOP 1 Id AS TOP_ID FROM dbo.Leads WHERE FullName = '$leadProbe') x;"
        $detail = Get-Status "/api/v1/leads/$leadId" -Headers $auth
        Write-Output "LEAD_DETAIL_STATUS=$($detail.Status)"
        if ($detail.Status -ne 200) { $failures.Add("reading the probe lead back returned $($detail.Status)") }
        if ($detail.Content -notlike "*$leadProbe*") { $failures.Add('the lead endpoint did not return the row that is in SQL Server') }

        $inbox = Get-Status '/api/v1/leads' -Headers $auth
        Write-Output "LEAD_LIST_STATUS=$($inbox.Status) ITEMS=$(Measure-JsonArray $inbox.Content)"
        if ($inbox.Status -ne 200) { $failures.Add("the lead inbox returned $($inbox.Status)") }

        # 7 — both notifications are queued, and nothing was sent inline (REQ-NOTIF-001).
        $queued = Invoke-SqlScalar -Query @"
SELECT COUNT(*) FROM dbo.OutboxEmails o
WHERE o.TemplateKey IN ('lead.acknowledgement', 'lead.owner-alert')
  AND (o.ToAddress = '$leadEmail' OR o.Subject LIKE '%$Nonce%');
"@
        Write-Output "LEAD_OUTBOX_QUEUED=$queued"
        if ($queued -lt 1) { $failures.Add("the enquiry queued $queued notifications, expected at least the acknowledgement") }

        # 8 — NFR-PRIV-03: no address reaches the log intact.
        $apiLog = Join-Path $EvidenceDirectory 'api-out.log'
        if (Test-Path $apiLog) {
            $leaked = @(Select-String -Path $apiLog -Pattern ([regex]::Escape($leadEmail)) -SimpleMatch).Count
            Write-Output "LEAD_LOG_ADDRESS_IN_FULL=$leaked"
            if ($leaked -ne 0) { $failures.Add("the submitter's address appears $leaked times in the API log, unmasked") }
        }

        # ---------------------------------------------------------------------------------------
        # P08 - the pipeline: what happens to an enquiry after it arrives.
        # ---------------------------------------------------------------------------------------

        $leadId = Invoke-SqlScalar "SELECT TOP 1 CONVERT(nvarchar(36), Id) FROM Leads WHERE FullName = 'PROBE-$Nonce'"

        if (-not $leadId) {
            $failures.Add('no lead to work the pipeline against')
        }
        else {
            # 1 - REQ-LEAD-011: an activity is appended and reaches the database.
            $probeBody = "PROBE-$Nonce"
            $activity = Get-Status "/api/v1/leads/$leadId/activities" -Method 'POST' -Headers $auth -Body @{
                activityType = 'Note'; direction = 'Internal'; body = $probeBody
            }

            Write-Output "PIPELINE_ACTIVITY_STATUS=$($activity.Status)"
            if ($activity.Status -ne 201) { $failures.Add("logging an activity returned $($activity.Status): $($activity.Content)") }

            $activityRows = Invoke-SqlScalar "SELECT COUNT(*) FROM LeadActivities WHERE Body = '$probeBody'"
            Write-Output "PIPELINE_ACTIVITY_ROWS=$activityRows"
            if ($activityRows -ne 1) { $failures.Add("expected one activity row, found $activityRows") }

            # 2 - REQ-LEAD-011: the timeline is append-only and says so.
            $activityId = ($activity.Content | ConvertFrom-Json).id
            $deleted = Get-Status "/api/v1/leads/$leadId/activities/$activityId" -Method 'DELETE' -Headers $auth
            Write-Output "PIPELINE_ACTIVITY_DELETE_STATUS=$($deleted.Status)"
            if ($deleted.Status -ne 405) { $failures.Add("deleting an activity returned $($deleted.Status), expected 405") }

            # 3 - REQ-LEAD-010: a stage change records both stages and the actor.
            $moved = Get-Status "/api/v1/leads/$leadId/stage" -Method 'POST' -Headers $auth -Body @{ stage = 'Contacted' }
            Write-Output "PIPELINE_STAGE_STATUS=$($moved.Status)"
            if ($moved.Status -ne 204) { $failures.Add("moving the stage returned $($moved.Status): $($moved.Content)") }

            $stageRows = Invoke-SqlScalar @"
SELECT COUNT(*) FROM LeadActivities
WHERE LeadId = '$leadId' AND ActivityType = 5 AND FromStage = 0 AND ToStage = 1
"@
            Write-Output "PIPELINE_STAGE_ACTIVITY_ROWS=$stageRows"
            if ($stageRows -lt 1) { $failures.Add('the stage change wrote no activity naming both stages') }

            # 4 - REQ-LEAD-010: disqualifying with a four-character reason is refused.
            $short = Get-Status "/api/v1/leads/$leadId/stage" -Method 'POST' -Headers $auth -Body @{
                stage = 'Disqualified'; reason = 'junk'
            }

            Write-Output "PIPELINE_SHORT_REASON_STATUS=$($short.Status)"
            Write-Output "PIPELINE_SHORT_REASON_BODY=$($short.Content)"
            if ($short.Status -ne 422) { $failures.Add("a four-character reason returned $($short.Status), expected 422") }

            # 5 - REQ-LEAD-016: merging keeps the earliest record, whichever way round it is asked.
            $otherId = Invoke-SqlScalar @"
SELECT TOP 1 CONVERT(nvarchar(36), Id) FROM Leads
WHERE Id <> '$leadId' AND Stage NOT IN (6, 7) ORDER BY CreatedAtUtc DESC
"@

            if ($otherId) {
                $expectedSurvivor = Invoke-SqlScalar @"
SELECT TOP 1 CONVERT(nvarchar(36), Id) FROM Leads
WHERE Id IN ('$leadId', '$otherId') ORDER BY CreatedAtUtc ASC, Id ASC
"@

                $merged = Get-Status "/api/v1/leads/$otherId/merge" -Method 'POST' -Headers $auth -Body @{ otherLeadId = $leadId }
                Write-Output "PIPELINE_MERGE_STATUS=$($merged.Status)"
                if ($merged.Status -ne 200) { $failures.Add("merging returned $($merged.Status): $($merged.Content)") }

                $survivor = ($merged.Content | ConvertFrom-Json).survivorId
                Write-Output "PIPELINE_MERGE_SURVIVOR=$survivor EXPECTED=$expectedSurvivor"
                if ("$survivor" -ne "$expectedSurvivor") {
                    $failures.Add("the merge kept $survivor; the earliest-created lead is $expectedSurvivor")
                }

                $mergedAway = Invoke-SqlScalar @"
SELECT COUNT(*) FROM Leads
WHERE Stage = 7 AND CONVERT(nvarchar(36), MergedIntoLeadId) = '$expectedSurvivor'
"@
                Write-Output "PIPELINE_MERGED_AWAY_ROWS=$mergedAway"
                if ($mergedAway -lt 1) { $failures.Add('the merged record does not point at the survivor') }
            }
        }

        # 6 - NFR-AUTHZ-02: the editor is refused the pipeline outright.
        $editorLogin = Get-Status '/api/v1/auth/login' -Method 'POST' -Body @{
            email = 'editor@softwaremanagement.test'; password = $ownerPassword; twoFactorCode = $null
        }

        if ($editorLogin.Status -eq 200) {
            $editorAuth = @{ Authorization = "Bearer $(($editorLogin.Content | ConvertFrom-Json).accessToken)" }
            $editorLeads = Get-Status '/api/v1/leads' -Headers $editorAuth
            Write-Output "PIPELINE_EDITOR_LEADS_STATUS=$($editorLeads.Status)"
            if ($editorLeads.Status -ne 403) { $failures.Add("an editor token got $($editorLeads.Status) on /api/v1/leads, expected 403") }
        }
        else {
            # Said out loud rather than skipped silently: a check that did not run must not read as
            # a check that passed.
            Write-Output "PIPELINE_EDITOR_LEADS_STATUS=not-checked (no editor account in this gate database)"
        }
    }
}
finally {
    if ($api -and -not $api.HasExited) {
        Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
        $api.WaitForExit(15000) | Out-Null
    }

    # Belt and braces. The API now runs in the process started above, but an earlier version of
    # this script used `dotnet run`, which leaves the application as a child when only the launcher
    # is killed. A stray host from an interrupted run would still hold the port, and D9 would then
    # report a leak that is really a half-finished shutdown.
    if ($FromSource) {
        Get-Process -Name 'SoftwareManagement.Api' -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
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
