[CmdletBinding()]
param(
    [switch]$StartStack,
    [string]$ApiBaseUri,
    [string]$GameHost = "127.0.0.1",
    [int]$GamePort = 0,
    [string]$ElasticsearchUri,
    [string]$KibanaUri,
    [string]$LogstashUri,
    [string]$ApmServerUri,
    [int]$IngestionTimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeArguments = @(
    "compose",
    "--project-directory", $repositoryRoot,
    "-f", (Join-Path $repositoryRoot "compose.yaml"),
    "-f", (Join-Path $repositoryRoot "compose.observability.yaml")
)

function Write-Step {
    param([string]$Message)

    Write-Host "[observability-smoke] $Message"
}

function Invoke-Docker {
    param([string[]]$Arguments)

    $output = @(& docker @Arguments 2>&1)
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $detail = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "docker command failed (ExitCode=$exitCode).$([Environment]::NewLine)$detail"
    }

    return $output
}

function Get-ComposeContainerId {
    param([string]$Service)

    $arguments = $composeArguments + @("ps", "-a", "-q", $Service)
    $containerId = Invoke-Docker -Arguments $arguments |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -match "^[0-9a-f]{12,64}$" } |
        Select-Object -Last 1

    if ([string]::IsNullOrWhiteSpace($containerId)) {
        throw "Compose service container was not found. Service=$Service"
    }

    return $containerId
}

function Get-ComposePublishedPort {
    param(
        [string]$Service,
        [int]$ContainerPort
    )

    $arguments = $composeArguments + @(
        "port",
        $Service,
        $ContainerPort.ToString()
    )
    $portLine = Invoke-Docker -Arguments $arguments |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -match ":([0-9]+)$" } |
        Select-Object -First 1

    $portMatch = [regex]::Match(
        [string]$portLine,
        ":(?<Port>[0-9]+)$")

    if ([string]::IsNullOrWhiteSpace($portLine) -or
        -not $portMatch.Success) {
        throw "Published port was not found. Service=$Service, ContainerPort=$ContainerPort"
    }

    return [int]$portMatch.Groups["Port"].Value
}

function Assert-ComposeService {
    param(
        [string]$Service,
        [switch]$CompletedSuccessfully
    )

    $containerId = Get-ComposeContainerId -Service $Service
    $stateJson = Invoke-Docker -Arguments @(
        "inspect",
        "--format", "{{json .State}}",
        $containerId
    ) | Select-Object -Last 1
    $state = $stateJson.ToString() | ConvertFrom-Json

    if ($CompletedSuccessfully) {
        if ($state.Status -ne "exited" -or $state.ExitCode -ne 0) {
            throw "Compose one-shot service did not complete successfully. Service=$Service, Status=$($state.Status), ExitCode=$($state.ExitCode)"
        }

        return
    }

    if ($state.Status -ne "running") {
        throw "Compose service is not running. Service=$Service, Status=$($state.Status)"
    }

    $healthProperty = $state.PSObject.Properties["Health"]

    if ($null -ne $healthProperty -and
        $healthProperty.Value.Status -ne "healthy") {
        throw "Compose service is not healthy. Service=$Service, Health=$($healthProperty.Value.Status)"
    }
}

function Assert-HttpEndpoint {
    param(
        [string]$Name,
        [string]$Uri
    )

    try {
        Invoke-RestMethod -Method Get -Uri $Uri -TimeoutSec 10 | Out-Null
    }
    catch {
        throw "$Name endpoint check failed. Uri=$Uri, Error=$($_.Exception.Message)"
    }
}

function Invoke-JsonRequest {
    param(
        [string]$Name,
        [string]$Uri,
        [hashtable]$Body
    )

    try {
        return Invoke-RestMethod `
            -Method Post `
            -Uri $Uri `
            -ContentType "application/json" `
            -Body ($Body | ConvertTo-Json -Compress) `
            -TimeoutSec 15
    }
    catch {
        throw "$Name request failed. Uri=$Uri, Error=$($_.Exception.Message)"
    }
}

function Invoke-AuthorizedJsonGet {
    param(
        [string]$Name,
        [string]$Uri,
        [string]$AccessToken
    )

    try {
        return Invoke-RestMethod `
            -Method Get `
            -Uri $Uri `
            -Headers @{
                Authorization = "Bearer $AccessToken"
            } `
            -TimeoutSec 15
    }
    catch {
        throw "$Name request failed. Uri=$Uri, Error=$($_.Exception.Message)"
    }
}

function New-UInt16LittleEndianBytes {
    param([int]$Value)

    if ($Value -lt 0 -or $Value -gt [UInt16]::MaxValue) {
        throw "Value is outside the UInt16 range. Value=$Value"
    }

    return ,([byte[]]@(
        [byte]($Value -band 0xFF),
        [byte](($Value -shr 8) -band 0xFF)
    ))
}

function Get-UInt16LittleEndian {
    param(
        [byte[]]$Buffer,
        [int]$Offset
    )

    if ($Offset -lt 0 -or $Offset + 2 -gt $Buffer.Length) {
        throw "Packet range cannot provide a UInt16. Offset=$Offset, Length=$($Buffer.Length)"
    }

    return [int]($Buffer[$Offset] -bor ($Buffer[$Offset + 1] -shl 8))
}

function New-GamePacket {
    param(
        [int]$Opcode,
        [byte[]]$Payload = @()
    )

    $packetSize = 4 + $Payload.Length

    if ($packetSize -gt 4096) {
        throw "TCP packet exceeds the maximum size. Size=$packetSize"
    }

    $packet = [byte[]]::new($packetSize)
    $sizeBytes = New-UInt16LittleEndianBytes -Value $packetSize
    $opcodeBytes = New-UInt16LittleEndianBytes -Value $Opcode

    [Array]::Copy($sizeBytes, 0, $packet, 0, 2)
    [Array]::Copy($opcodeBytes, 0, $packet, 2, 2)

    if ($Payload.Length -gt 0) {
        [Array]::Copy($Payload, 0, $packet, 4, $Payload.Length)
    }

    return ,$packet
}

function Read-Exactly {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int]$Count
    )

    $buffer = [byte[]]::new($Count)
    $offset = 0

    while ($offset -lt $Count) {
        $read = $Stream.Read($buffer, $offset, $Count - $offset)

        if ($read -eq 0) {
            throw "Server closed the connection while receiving a TCP packet. Received=$offset, Expected=$Count"
        }

        $offset += $read
    }

    return ,$buffer
}

function Read-GamePacket {
    param([System.Net.Sockets.NetworkStream]$Stream)

    $sizeBytes = Read-Exactly -Stream $Stream -Count 2
    $packetSize = Get-UInt16LittleEndian -Buffer $sizeBytes -Offset 0

    if ($packetSize -lt 4 -or $packetSize -gt 4096) {
        throw "Server sent a TCP packet outside the allowed size range. Size=$packetSize"
    }

    $remaining = Read-Exactly -Stream $Stream -Count ($packetSize - 2)
    $packet = [byte[]]::new($packetSize)
    [Array]::Copy($sizeBytes, 0, $packet, 0, 2)
    [Array]::Copy($remaining, 0, $packet, 2, $remaining.Length)

    return ,$packet
}

function Send-GamePacket {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [byte[]]$Packet
    )

    $Stream.Write($Packet, 0, $Packet.Length)
    $Stream.Flush()
}

function Invoke-GameTcpScenario {
    param([string]$AccessToken)

    $tokenBytes = [Text.Encoding]::UTF8.GetBytes($AccessToken)

    if ($tokenBytes.Length -gt [UInt16]::MaxValue) {
        throw "Access token exceeds the TCP string length limit."
    }

    $tokenLengthBytes = New-UInt16LittleEndianBytes -Value $tokenBytes.Length
    $loginPayload = [byte[]]::new(2 + $tokenBytes.Length)
    [Array]::Copy($tokenLengthBytes, 0, $loginPayload, 0, 2)
    [Array]::Copy($tokenBytes, 0, $loginPayload, 2, $tokenBytes.Length)

    $client = [System.Net.Sockets.TcpClient]::new()
    $client.NoDelay = $true
    $client.ReceiveTimeout = 10000
    $client.SendTimeout = 10000

    try {
        $client.Connect($GameHost, $GamePort)
        $stream = $client.GetStream()

        Send-GamePacket `
            -Stream $stream `
            -Packet (New-GamePacket -Opcode 1 -Payload $loginPayload)

        $loginResponse = Read-GamePacket -Stream $stream
        $loginOpcode = Get-UInt16LittleEndian -Buffer $loginResponse -Offset 2

        if ($loginOpcode -ne 2 -or $loginResponse.Length -lt 5 -or $loginResponse[4] -ne 1) {
            throw "TCP login response was not successful. Opcode=$loginOpcode"
        }

        Send-GamePacket `
            -Stream $stream `
            -Packet (New-GamePacket -Opcode 15)

        $profileResponse = Read-GamePacket -Stream $stream
        $profileOpcode = Get-UInt16LittleEndian -Buffer $profileResponse -Offset 2

        if ($profileOpcode -ne 16 -or $profileResponse.Length -lt 5 -or $profileResponse[4] -ne 1) {
            throw "TCP PlayerProfile response was not successful. Opcode=$profileOpcode"
        }
    }
    finally {
        $client.Dispose()
    }
}

function Wait-ElasticsearchMatch {
    param(
        [string]$Name,
        [string]$IndexPattern,
        [hashtable[]]$Filters,
        [DateTimeOffset]$Deadline
    )

    $uri = "$($ElasticsearchUri.TrimEnd('/'))/$IndexPattern/_search?ignore_unavailable=true&allow_no_indices=true"
    $lastError = $null

    do {
        $body = @{
            size = 0
            track_total_hits = $true
            query = @{
                bool = @{
                    filter = $Filters
                }
            }
        } | ConvertTo-Json -Depth 10 -Compress

        try {
            $response = Invoke-RestMethod `
                -Method Post `
                -Uri $uri `
                -ContentType "application/json" `
                -Body $body `
                -TimeoutSec 10

            if ([Int64]$response.hits.total.value -gt 0) {
                Write-Step "$Name found"
                return
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    }
    while ([DateTimeOffset]::UtcNow -lt $Deadline)

    $errorSuffix = if ($null -eq $lastError) {
        "No matching document was found."
    }
    else {
        "Last search error: $lastError"
    }

    throw "$Name verification timed out. $errorSuffix"
}

$startedAt = [DateTimeOffset]::UtcNow.AddSeconds(-5)
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($IngestionTimeoutSeconds)
$nickname = "obs$([Guid]::NewGuid().ToString('N').Substring(0, 7))"
$password = "$([Guid]::NewGuid().ToString('N'))aA1!"
$accessToken = $null

try {
    if ($StartStack) {
        Write-Step "Starting the Compose observability stack"
        Invoke-Docker -Arguments ($composeArguments + @("up", "-d", "--build")) | Out-Null
    }

    Write-Step "Checking Compose service states"
    foreach ($service in @(
        "postgres",
        "redis",
        "silo-1",
        "silo-2",
        "api",
        "game",
        "elasticsearch",
        "kibana",
        "logstash",
        "filebeat",
        "apm-server"
    )) {
        Assert-ComposeService -Service $service
    }
    Assert-ComposeService -Service "migration" -CompletedSuccessfully

    if ([string]::IsNullOrWhiteSpace($ApiBaseUri)) {
        $ApiBaseUri = "http://localhost:$(Get-ComposePublishedPort -Service 'api' -ContainerPort 8080)"
    }

    if ($GamePort -eq 0) {
        $GamePort = Get-ComposePublishedPort -Service "game" -ContainerPort 7777
    }

    if ([string]::IsNullOrWhiteSpace($ElasticsearchUri)) {
        $ElasticsearchUri = "http://localhost:$(Get-ComposePublishedPort -Service 'elasticsearch' -ContainerPort 9200)"
    }

    if ([string]::IsNullOrWhiteSpace($KibanaUri)) {
        $KibanaUri = "http://localhost:$(Get-ComposePublishedPort -Service 'kibana' -ContainerPort 5601)"
    }

    if ([string]::IsNullOrWhiteSpace($LogstashUri)) {
        $LogstashUri = "http://localhost:$(Get-ComposePublishedPort -Service 'logstash' -ContainerPort 9600)"
    }

    if ([string]::IsNullOrWhiteSpace($ApmServerUri)) {
        $ApmServerUri = "http://localhost:$(Get-ComposePublishedPort -Service 'apm-server' -ContainerPort 8200)"
    }

    Write-Step "Checking observability endpoints"
    Assert-HttpEndpoint `
        -Name "Elasticsearch" `
        -Uri "$($ElasticsearchUri.TrimEnd('/'))/_cluster/health"
    Assert-HttpEndpoint `
        -Name "Kibana" `
        -Uri "$($KibanaUri.TrimEnd('/'))/api/status"
    Assert-HttpEndpoint `
        -Name "Logstash" `
        -Uri "$($LogstashUri.TrimEnd('/'))/_node/pipelines/main"
    Assert-HttpEndpoint `
        -Name "APM Server" `
        -Uri $ApmServerUri

    Write-Step "Creating API traces with a temporary Player register/login"
    Invoke-JsonRequest `
        -Name "register" `
        -Uri "$($ApiBaseUri.TrimEnd('/'))/register" `
        -Body @{
            nickname = $nickname
            password = $password
        } | Out-Null

    $login = Invoke-JsonRequest `
        -Name "login" `
        -Uri "$($ApiBaseUri.TrimEnd('/'))/login" `
        -Body @{
            nickname = $nickname
            password = $password
        }

    $accessToken = $login.accessToken

    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw "Login response does not contain accessToken."
    }

    Write-Step "Creating API PlayerProfile trace"
    $profile = Invoke-AuthorizedJsonGet `
        -Name "player profile" `
        -Uri "$($ApiBaseUri.TrimEnd('/'))/players/me/profile" `
        -AccessToken $accessToken

    if ($null -eq $profile -or [Int64]$profile.id -le 0) {
        throw "PlayerProfile response does not contain a valid player id."
    }

    Write-Step "Creating Game transactions with TCP login/PlayerProfile"
    Invoke-GameTcpScenario -AccessToken $accessToken

    $timeFilter = @{
        range = @{
            "@timestamp" = @{
                gte = $startedAt.ToString("o")
            }
        }
    }

    Write-Step "Waiting for Elasticsearch ingestion"
    Wait-ElasticsearchMatch `
        -Name "API structured log" `
        -IndexPattern "blue-server-logs-*" `
        -Filters @(
            @{ term = @{ "service.name" = "blue-server-api" } },
            $timeFilter
        ) `
        -Deadline $deadline

    Wait-ElasticsearchMatch `
        -Name "Game structured log" `
        -IndexPattern "blue-server-logs-*" `
        -Filters @(
            @{ term = @{ "service.name" = "blue-server-game" } },
            $timeFilter
        ) `
        -Deadline $deadline

    Wait-ElasticsearchMatch `
        -Name "API APM transaction" `
        -IndexPattern "traces-apm*,apm-*" `
        -Filters @(
            @{ term = @{ "service.name" = "blue-server-api" } },
            @{ term = @{ "processor.event" = "transaction" } },
            $timeFilter
        ) `
        -Deadline $deadline

    Wait-ElasticsearchMatch `
        -Name "Game TCP APM transaction" `
        -IndexPattern "traces-apm*,apm-*" `
        -Filters @(
            @{ term = @{ "service.name" = "blue-server-game" } },
            @{ term = @{ "processor.event" = "transaction" } },
            @{ term = @{ "transaction.type" = "tcp" } },
            $timeFilter
        ) `
        -Deadline $deadline

    Write-Step "Success: API/Game logs and APM transactions were collected"
}
finally {
    $accessToken = $null
    $password = $null
}
