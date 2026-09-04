param(
    [Parameter(Mandatory = $true)]
    [uri]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [guid]$LicensePlanId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('SEPAY_TEST_ONLY')]
    [string]$Confirmation,

    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string]$UserTokenEnvironmentVariable = 'VIDEOMAKER_TEST_USER_TOKEN',

    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string]$AdminTokenEnvironmentVariable = 'VIDEOMAKER_TEST_ADMIN_TOKEN',

    [ValidateRange(2, 32)]
    [int]$ConcurrentWebhookCount = 8,

    [switch]$AllowRemote
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-EndpointUri {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    return [uri]::new($script:normalizedBaseUri, $RelativePath)
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpMethod]$Method,
        [Parameter(Mandatory = $true)]
        [uri]$Uri,
        [object]$Body,
        [string]$BearerToken = ''
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        $request.Headers.Accept.ParseAdd('application/json')
        if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
            $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new(
                'Bearer',
                $BearerToken)
        }
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            $request.Content = [System.Net.Http.StringContent]::new(
                $json,
                [System.Text.Encoding]::UTF8,
                'application/json')
        }

        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "Request $($Method.Method) $($Uri.AbsolutePath) failed with HTTP $([int]$response.StatusCode)."
            }
            if ([string]::IsNullOrWhiteSpace($responseBody)) {
                return $null
            }
            return $responseBody | ConvertFrom-Json
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function New-WebhookPayload {
    param(
        [Parameter(Mandatory = $true)]
        [long]$TransactionId,
        [Parameter(Mandatory = $true)]
        [string]$AccountNumber,
        [Parameter(Mandatory = $true)]
        [string]$TransferCode,
        [Parameter(Mandatory = $true)]
        [decimal]$Amount,
        [string]$TransferType = 'in'
    )

    return [ordered]@{
        id = $TransactionId
        gateway = 'VIDEOMAKER_TEST'
        transactionDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')
        accountNumber = $AccountNumber
        subAccount = ''
        code = $TransferCode
        content = "TEST $TransferCode"
        transferType = $TransferType
        description = "VideoMaker simulated payment $TransferCode"
        transferAmount = $Amount
        accumulated = $Amount
        referenceCode = "SIM-$TransactionId"
    }
}

function Get-PaymentStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OrderCode
    )

    $escapedOrderCode = [uri]::EscapeDataString($OrderCode)
    return Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Uri (New-EndpointUri "api/license/payments/$escapedOrderCode/status") `
        -BearerToken $script:userToken
}

function Assert-PaymentStillPending {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OrderCode,
        [Parameter(Mandatory = $true)]
        [string]$Scenario
    )

    $status = Get-PaymentStatus -OrderCode $OrderCode
    Assert-Condition ($status.status -eq 'Pending') "$Scenario changed payment status to '$($status.status)'."
    Assert-Condition (-not $status.isPaid) "$Scenario marked the payment as paid."
    Assert-Condition (-not $status.isFulfilled) "$Scenario fulfilled the payment."
}

if (-not $BaseUrl.IsAbsoluteUri) {
    throw 'BaseUrl must be an absolute URI.'
}
if (-not [string]::IsNullOrEmpty($BaseUrl.UserInfo) -or
    -not [string]::IsNullOrEmpty($BaseUrl.Query) -or
    -not [string]::IsNullOrEmpty($BaseUrl.Fragment)) {
    throw 'BaseUrl cannot contain user info, a query string, or a fragment.'
}
if ($BaseUrl.Scheme -notin @('http', 'https')) {
    throw 'BaseUrl must use HTTP or HTTPS.'
}
if (-not $BaseUrl.IsLoopback) {
    if (-not $AllowRemote) {
        throw 'Remote targets are blocked. Use -AllowRemote only for an isolated staging environment.'
    }
    if ($BaseUrl.Scheme -ne 'https') {
        throw 'A remote test target must use HTTPS.'
    }
}

$script:normalizedBaseUri = [uri]($BaseUrl.AbsoluteUri.TrimEnd('/') + '/')
$script:userToken = [Environment]::GetEnvironmentVariable($UserTokenEnvironmentVariable)
$adminToken = [Environment]::GetEnvironmentVariable($AdminTokenEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($script:userToken)) {
    throw "The user JWT environment variable '$UserTokenEnvironmentVariable' is empty."
}
if ([string]::IsNullOrWhiteSpace($adminToken)) {
    throw "The Global Admin JWT environment variable '$AdminTokenEnvironmentVariable' is empty."
}

$script:userClient = [System.Net.Http.HttpClient]::new()
$webhookClient = [System.Net.Http.HttpClient]::new()
$adminClient = [System.Net.Http.HttpClient]::new()
try {
    $licenseBefore = Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Uri (New-EndpointUri 'api/license/current') `
        -BearerToken $script:userToken
    Assert-Condition (-not $licenseBefore.hasActiveLicense) 'The test user already has an active license.'
    Assert-Condition ($licenseBefore.accessState -in @('Missing', 'Expired')) `
        "The test user must have Missing or Expired access; actual state is '$($licenseBefore.accessState)'."

    $currentPayment = Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Uri (New-EndpointUri 'api/license/payments/current') `
        -BearerToken $script:userToken
    Assert-Condition ($null -eq $currentPayment.payment) 'The test user already has an open payment.'

    $offers = @(
        Invoke-JsonRequest `
            -Client $script:userClient `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Uri (New-EndpointUri 'api/license/offers') `
            -BearerToken $script:userToken
    )
    $planIdText = $LicensePlanId.ToString('D')
    $offer = $offers | Where-Object { $_.licensePlanId -eq $planIdText } | Select-Object -First 1
    Assert-Condition ($null -ne $offer) 'The selected license plan is not a public offer.'
    Assert-Condition ($offer.organizationSeatAvailable -eq $true) 'The selected license plan has no organization seat available.'

    $idempotencyKey = "sepay-sim-$([Guid]::NewGuid().ToString('N'))"
    $checkout = Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Uri (New-EndpointUri 'api/license/payments') `
        -Body ([ordered]@{
            licensePlanId = $planIdText
            idempotencyKey = $idempotencyKey
        }) `
        -BearerToken $script:userToken
    Assert-Condition ($checkout.status -eq 'Pending') "New payment status is '$($checkout.status)' instead of Pending."
    Assert-Condition ($checkout.provisioningStatus -eq 'Reserved') `
        "New assignment status is '$($checkout.provisioningStatus)' instead of Reserved."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$checkout.assignedOrganizationId)) `
        'The payment did not reserve an organization.'

    $transactionSeed = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() * 100
    $webhookUri = New-EndpointUri 'api/payments/sepay/webhook'

    $invalidDirection = New-WebhookPayload `
        -TransactionId ($transactionSeed + 1) `
        -AccountNumber $checkout.receiverAccountNumber `
        -TransferCode $checkout.transferCode `
        -Amount $checkout.amountVnd `
        -TransferType 'out'
    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $invalidDirection
    Assert-PaymentStillPending -OrderCode $checkout.orderCode -Scenario 'Outgoing webhook'

    $invalidAccount = New-WebhookPayload `
        -TransactionId ($transactionSeed + 2) `
        -AccountNumber ($checkout.receiverAccountNumber + '9') `
        -TransferCode $checkout.transferCode `
        -Amount $checkout.amountVnd
    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $invalidAccount
    Assert-PaymentStillPending -OrderCode $checkout.orderCode -Scenario 'Wrong-account webhook'

    $invalidCode = New-WebhookPayload `
        -TransactionId ($transactionSeed + 3) `
        -AccountNumber $checkout.receiverAccountNumber `
        -TransferCode 'UNKNOWNTESTCODE' `
        -Amount $checkout.amountVnd
    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $invalidCode
    Assert-PaymentStillPending -OrderCode $checkout.orderCode -Scenario 'Wrong-code webhook'

    $invalidAmount = New-WebhookPayload `
        -TransactionId ($transactionSeed + 4) `
        -AccountNumber $checkout.receiverAccountNumber `
        -TransferCode $checkout.transferCode `
        -Amount ([decimal]$checkout.amountVnd + [decimal]0.5)
    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $invalidAmount
    Assert-PaymentStillPending -OrderCode $checkout.orderCode -Scenario 'Wrong-amount webhook'

    $providerTransactionId = $transactionSeed + 10
    $validWebhook = New-WebhookPayload `
        -TransactionId $providerTransactionId `
        -AccountNumber $checkout.receiverAccountNumber `
        -TransferCode $checkout.transferCode `
        -Amount $checkout.amountVnd
    $validWebhookJson = $validWebhook | ConvertTo-Json -Depth 8 -Compress
    $tasks = [System.Collections.Generic.List[System.Threading.Tasks.Task]]::new()
    $contents = [System.Collections.Generic.List[System.IDisposable]]::new()
    for ($index = 0; $index -lt $ConcurrentWebhookCount; $index++) {
        $content = [System.Net.Http.StringContent]::new(
            $validWebhookJson,
            [System.Text.Encoding]::UTF8,
            'application/json')
        $contents.Add($content)
        $tasks.Add($webhookClient.PostAsync($webhookUri, $content))
    }
    try {
        [System.Threading.Tasks.Task]::WaitAll($tasks.ToArray())
        foreach ($task in $tasks) {
            $response = $task.GetType().GetProperty('Result').GetValue($task)
            try {
                Assert-Condition $response.IsSuccessStatusCode `
                    "Concurrent webhook returned HTTP $([int]$response.StatusCode)."
            }
            finally {
                $response.Dispose()
            }
        }
    }
    finally {
        foreach ($content in $contents) {
            $content.Dispose()
        }
    }

    $status = Get-PaymentStatus -OrderCode $checkout.orderCode
    Assert-Condition ($status.status -eq 'Fulfilled') "Payment status is '$($status.status)' instead of Fulfilled."
    Assert-Condition ($status.isPaid -and $status.isFulfilled) 'Payment flags do not report successful fulfillment.'
    Assert-Condition ($status.provisioningStatus -eq 'Active') `
        "Assignment status is '$($status.provisioningStatus)' instead of Active."
    Assert-Condition ($status.assignedOrganizationId -eq $checkout.assignedOrganizationId) `
        'The fulfilled organization differs from the reserved organization.'

    $licenseAfter = Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Uri (New-EndpointUri 'api/license/current') `
        -BearerToken $script:userToken
    Assert-Condition ($licenseAfter.hasActiveLicense -eq $true) 'The test user did not receive an active license.'
    Assert-Condition ($licenseAfter.assignedOrganizationId -eq $checkout.assignedOrganizationId) `
        'The current license does not expose the assigned organization.'
    $licenseExpiry = [string]$licenseAfter.expiresAtUtc

    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $validWebhook
    $replayedWithNewId = New-WebhookPayload `
        -TransactionId ($providerTransactionId + 1) `
        -AccountNumber $checkout.receiverAccountNumber `
        -TransferCode $checkout.transferCode `
        -Amount $checkout.amountVnd
    $null = Invoke-JsonRequest -Client $webhookClient -Method ([System.Net.Http.HttpMethod]::Post) -Uri $webhookUri -Body $replayedWithNewId
    $licenseAfterReplay = Invoke-JsonRequest `
        -Client $script:userClient `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Uri (New-EndpointUri 'api/license/current') `
        -BearerToken $script:userToken
    Assert-Condition ([string]$licenseAfterReplay.expiresAtUtc -eq $licenseExpiry) `
        'A replayed webhook changed the license expiry.'

    $escapedOrderCode = [uri]::EscapeDataString([string]$checkout.orderCode)
    $adminPayments = @(
        Invoke-JsonRequest `
            -Client $adminClient `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Uri (New-EndpointUri "api/admin/licenses/payments?search=$escapedOrderCode&take=10") `
            -BearerToken $adminToken
    )
    Assert-Condition ($adminPayments.Count -eq 1) `
        "Admin reconciliation returned $($adminPayments.Count) payments instead of one."
    $adminPayment = $adminPayments[0]
    Assert-Condition ($adminPayment.status -eq 'Fulfilled') 'Admin reconciliation does not report Fulfilled.'
    Assert-Condition ([long]$adminPayment.providerTransactionId -eq $providerTransactionId) `
        'The stored provider transaction ID differs from the accepted webhook.'
    foreach ($forbiddenProperty in @(
        'receiverBankCodeSnapshot',
        'receiverAccountNumberSnapshot',
        'receiverAccountNameSnapshot',
        'idempotencyKey',
        'entitlementSnapshotJson',
        'providerReferenceCode')) {
        Assert-Condition ($forbiddenProperty -notin $adminPayment.PSObject.Properties.Name) `
            "Admin payment response exposes forbidden property '$forbiddenProperty'."
    }

    $assignments = @(
        Invoke-JsonRequest `
            -Client $adminClient `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Uri (New-EndpointUri 'api/admin/organization-pools/assignments?take=200') `
            -BearerToken $adminToken
    )
    $paymentAssignments = @($assignments | Where-Object { $_.licensePaymentId -eq $adminPayment.licensePaymentId })
    Assert-Condition ($paymentAssignments.Count -eq 1) `
        "Provisioning reconciliation returned $($paymentAssignments.Count) assignments instead of one."
    Assert-Condition ($paymentAssignments[0].status -eq 'Active') 'The organization assignment is not Active.'
    Assert-Condition ($paymentAssignments[0].membershipManaged -eq $true) `
        'The test assignment did not create an automatically managed membership.'

    [pscustomobject]@{
        Succeeded = $true
        OrderCode = $checkout.orderCode
        LicensePaymentId = $adminPayment.licensePaymentId
        ProviderTransactionId = $providerTransactionId
        OrganizationId = $checkout.assignedOrganizationId
        OrganizationName = $checkout.assignedOrganizationName
        ConcurrentWebhookCount = $ConcurrentWebhookCount
        PaymentStatus = $adminPayment.status
        AssignmentStatus = $paymentAssignments[0].status
    }
}
finally {
    $script:userClient.Dispose()
    $webhookClient.Dispose()
    $adminClient.Dispose()
}
