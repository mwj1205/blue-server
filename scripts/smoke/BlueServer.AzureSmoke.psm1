Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-AzureSmokeStep {
    param([string]$Message)

    Write-Host "[azure-smoke] $Message"
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    $output = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $outputText = ($output |
        ForEach-Object { $_.ToString() }) -join [Environment]::NewLine

    if ($exitCode -ne 0) {
        throw "Command failed. File=$FilePath, ExitCode=$exitCode$([Environment]::NewLine)$outputText"
    }

    return $outputText
}

function Invoke-AzureSmokeKubectl {
    param(
        [object]$Context,
        [string[]]$Arguments
    )

    return Invoke-NativeCommand `
        -FilePath $Context.KubectlPath `
        -Arguments (@(
            "--context",
            $Context.KubernetesContext
        ) + $Arguments)
}

function ConvertFrom-CommandJson {
    param(
        [string]$Name,
        [string]$Json
    )

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "$Name returned an empty JSON response."
    }

    try {
        return $Json | ConvertFrom-Json
    }
    catch {
        throw "$Name returned invalid JSON. Error=$($_.Exception.Message)"
    }
}

function Confirm-HelmRelease {
    param([object]$Context)

    $statusJson = Invoke-NativeCommand `
        -FilePath $Context.HelmPath `
        -Arguments @(
            "status",
            $Context.ReleaseName,
            "--namespace",
            $Context.Namespace,
            "--kube-context",
            $Context.KubernetesContext,
            "--output",
            "json"
        )
    $status = ConvertFrom-CommandJson `
        -Name "Helm release $($Context.ReleaseName)" `
        -Json $statusJson

    if ($status.info.status -ne "deployed") {
        throw "Helm release is not deployed. Release=$($Context.ReleaseName), Status=$($status.info.status)"
    }

    return [pscustomobject]@{
        Revision = [int]$status.version
        Status = [string]$status.info.status
    }
}

function Get-ImageTag {
    param(
        [string]$DeploymentName,
        [string]$Image
    )

    $match = [regex]::Match($Image, ":(?<Tag>[^/:]+)$")

    if (-not $match.Success) {
        throw "Deployment image does not use a tag. Deployment=$DeploymentName, Image=$Image"
    }

    $tag = $match.Groups["Tag"].Value

    if ($tag -notmatch "^[0-9a-f]{40}$") {
        throw "Deployment image tag is not a full Git SHA. Deployment=$DeploymentName, Tag=$tag"
    }

    return $tag
}

function Confirm-DeploymentReady {
    param(
        [object]$Context,
        [string]$DeploymentName
    )

    $deploymentJson = Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "get",
            "deployment/$DeploymentName",
            "--namespace",
            $Context.Namespace,
            "--output",
            "json"
        )
    $deployment = ConvertFrom-CommandJson `
        -Name "Deployment $DeploymentName" `
        -Json $deploymentJson

    $desiredReplicas = [int]$deployment.spec.replicas
    $updatedReplicas = [int]$deployment.status.updatedReplicas
    $readyReplicas = [int]$deployment.status.readyReplicas
    $availableReplicas = [int]$deployment.status.availableReplicas

    if ($desiredReplicas -le 0 -or
        $updatedReplicas -ne $desiredReplicas -or
        $readyReplicas -ne $desiredReplicas -or
        $availableReplicas -ne $desiredReplicas) {
        throw "Deployment is not ready. Deployment=$DeploymentName, Desired=$desiredReplicas, Updated=$updatedReplicas, Ready=$readyReplicas, Available=$availableReplicas"
    }

    $image = [string]$deployment.spec.template.spec.containers[0].image

    return [pscustomobject]@{
        Name = $DeploymentName
        Replicas = $desiredReplicas
        Image = $image
        ImageTag = Get-ImageTag `
            -DeploymentName $DeploymentName `
            -Image $image
    }
}

function Confirm-ClusterIpService {
    param(
        [object]$Context,
        [string]$ServiceName,
        [int]$ExpectedPort
    )

    $serviceJson = Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "get",
            "service/$ServiceName",
            "--namespace",
            $Context.Namespace,
            "--output",
            "json"
        )
    $service = ConvertFrom-CommandJson `
        -Name "Service $ServiceName" `
        -Json $serviceJson

    if ($service.spec.type -ne "ClusterIP") {
        throw "Smoke Test requires a private ClusterIP service. Service=$ServiceName, Type=$($service.spec.type)"
    }

    $matchingPort = @($service.spec.ports |
        Where-Object { [int]$_.port -eq $ExpectedPort })

    if ($matchingPort.Count -ne 1) {
        throw "Expected service port was not found exactly once. Service=$ServiceName, Port=$ExpectedPort"
    }
}

function Confirm-LocalPortAvailable {
    param([int]$Port)

    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        $Port)

    try {
        $listener.Start()
    }
    catch {
        throw "Local port is already in use. Port=$Port"
    }
    finally {
        $listener.Stop()
    }
}

function Get-PortForwardFailureDetail {
    param([System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        return "kubectl port-forward is still running."
    }

    $standardOutput = $Process.StandardOutput.ReadToEnd().Trim()
    $standardError = $Process.StandardError.ReadToEnd().Trim()
    $detail = @($standardOutput, $standardError) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if ($detail.Count -eq 0) {
        return "kubectl port-forward exited without output. ExitCode=$($Process.ExitCode)"
    }

    return $detail -join [Environment]::NewLine
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    $asyncResult = $null

    try {
        $asyncResult = $client.BeginConnect(
            $HostName,
            $Port,
            $null,
            $null)

        if (-not $asyncResult.AsyncWaitHandle.WaitOne(
                $TimeoutMilliseconds)) {
            return $false
        }

        $client.EndConnect($asyncResult)
        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $asyncResult) {
            $asyncResult.AsyncWaitHandle.Close()
        }

        $client.Dispose()
    }
}

function Invoke-JsonPost {
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

function New-AzureSmokeContext {
    param(
        [string]$KubernetesContext,
        [string]$Namespace,
        [string]$ReleaseName
    )

    $kubectlCommand = Get-Command kubectl -ErrorAction Stop
    $helmCommand = Get-Command helm -ErrorAction Stop

    return [pscustomobject]@{
        PSTypeName = "BlueServer.AzureSmokeContext"
        KubernetesContext = $KubernetesContext
        Namespace = $Namespace
        ReleaseName = $ReleaseName
        KubectlPath = $kubectlCommand.Source
        HelmPath = $helmCommand.Source
    }
}

function Confirm-AzureSmokeDeployment {
    param([object]$Context)

    Write-AzureSmokeStep "Checking AKS context and namespace"
    Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "get",
            "namespace/$($Context.Namespace)",
            "--output",
            "name"
        ) | Out-Null

    Write-AzureSmokeStep "Checking Helm release"
    $release = Confirm-HelmRelease -Context $Context

    Write-AzureSmokeStep "Checking API, Game, and Silo deployments"
    $apiDeployment = Confirm-DeploymentReady `
        -Context $Context `
        -DeploymentName "$($Context.ReleaseName)-api"
    $gameDeployment = Confirm-DeploymentReady `
        -Context $Context `
        -DeploymentName "$($Context.ReleaseName)-game"
    $siloDeployment = Confirm-DeploymentReady `
        -Context $Context `
        -DeploymentName "$($Context.ReleaseName)-silo"

    $imageTags = @(@(
            $apiDeployment.ImageTag,
            $gameDeployment.ImageTag,
            $siloDeployment.ImageTag
        ) | Select-Object -Unique)

    if ($imageTags.Count -ne 1) {
        throw "Application deployments do not use the same Git SHA tag. API=$($apiDeployment.ImageTag), Game=$($gameDeployment.ImageTag), Silo=$($siloDeployment.ImageTag)"
    }

    if ($siloDeployment.Replicas -ne 2) {
        throw "Azure Smoke Test expects two Silo replicas. Actual=$($siloDeployment.Replicas)"
    }

    Write-AzureSmokeStep "Checking private ClusterIP services"
    Confirm-ClusterIpService `
        -Context $Context `
        -ServiceName "$($Context.ReleaseName)-api" `
        -ExpectedPort 80
    Confirm-ClusterIpService `
        -Context $Context `
        -ServiceName "$($Context.ReleaseName)-game" `
        -ExpectedPort 7777

    return [pscustomobject]@{
        HelmRevision = $release.Revision
        ImageTag = $imageTags[0]
        ApiReplicas = $apiDeployment.Replicas
        GameReplicas = $gameDeployment.Replicas
        SiloReplicas = $siloDeployment.Replicas
    }
}

function Start-AzureSmokePortForward {
    param(
        [object]$Context,
        [string]$ServiceName,
        [int]$LocalPort,
        [int]$RemotePort
    )

    Confirm-LocalPortAvailable -Port $LocalPort

    # Public Service 생성 없이 ClusterIP에 임시 연결
    $arguments = @(
        "--context",
        $Context.KubernetesContext,
        "port-forward",
        "service/$ServiceName",
        "${LocalPort}:${RemotePort}",
        "--namespace",
        $Context.Namespace,
        "--address",
        "127.0.0.1"
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Context.KubectlPath
    $startInfo.Arguments = $arguments -join " "
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    if (-not $process.Start()) {
        $process.Dispose()
        throw "Failed to start kubectl port-forward. Service=$ServiceName"
    }

    return $process
}

function Wait-AzureSmokePortForward {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$LocalPort,
        [int]$StartupTimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(
        $StartupTimeoutSeconds)

    do {
        $Process.Refresh()

        if ($Process.HasExited) {
            $detail = Get-PortForwardFailureDetail -Process $Process
            throw "kubectl port-forward exited before becoming ready.$([Environment]::NewLine)$detail"
        }

        if (Test-TcpPort `
                -HostName "127.0.0.1" `
                -Port $LocalPort `
                -TimeoutMilliseconds 500) {
            return
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "kubectl port-forward did not become ready. LocalPort=$LocalPort, TimeoutSeconds=$StartupTimeoutSeconds"
}

function Stop-AzureSmokePortForward {
    param(
        [AllowNull()]
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()

        if (-not $Process.HasExited) {
            $Process.Kill()
            $Process.WaitForExit(5000) | Out-Null
        }
    }
    finally {
        $Process.Dispose()
    }
}

function New-AzureSmokePlayerSession {
    param([string]$ApiBaseUri)

    $nickname = "smoke-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))-$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
    $password = "$([Guid]::NewGuid().ToString('N'))aA1!"
    $loginResponse = $null
    $accessToken = $null
    $refreshToken = $null

    try {
        Write-AzureSmokeStep "Registering a temporary Player"
        Invoke-JsonPost `
            -Name "Register" `
            -Uri "$($ApiBaseUri.TrimEnd('/'))/register" `
            -Body @{
                nickname = $nickname
                password = $password
            } | Out-Null

        Write-AzureSmokeStep "Logging in with the temporary Player"
        $loginResponse = Invoke-JsonPost `
            -Name "Login" `
            -Uri "$($ApiBaseUri.TrimEnd('/'))/login" `
            -Body @{
                nickname = $nickname
                password = $password
            }
        $accessToken = [string]$loginResponse.accessToken
        $refreshToken = [string]$loginResponse.refreshToken

        if ([string]::IsNullOrWhiteSpace($accessToken)) {
            throw "Login response does not contain an access token."
        }

        if ([string]::IsNullOrWhiteSpace($refreshToken)) {
            throw "Login response does not contain a refresh token."
        }

        return [pscustomobject]@{
            PSTypeName = "BlueServer.AzureSmokePlayerSession"
            Nickname = $nickname
            Password = $password
            AccessToken = $accessToken
            RefreshToken = $refreshToken
        }
    }
    finally {
        $loginResponse = $null
        $password = $null
        $accessToken = $null
        $refreshToken = $null
    }
}

function Clear-AzureSmokePlayerSession {
    param(
        [AllowNull()]
        [object]$Session
    )

    if ($null -eq $Session) {
        return
    }

    # Script 종료 전에 Process Memory의 인증 값 참조 제거
    foreach ($propertyName in @(
        "Password",
        "AccessToken",
        "RefreshToken"
    )) {
        if ($null -ne $Session.psobject.Properties[$propertyName]) {
            $Session.$propertyName = $null
        }
    }
}

function Get-AzureSmokePlayerProfile {
    param(
        [string]$ApiBaseUri,
        [string]$AccessToken
    )

    try {
        return Invoke-RestMethod `
            -Method Get `
            -Uri "$($ApiBaseUri.TrimEnd('/'))/players/me/profile" `
            -Headers @{
                Authorization = "Bearer $AccessToken"
            } `
            -TimeoutSec 15
    }
    catch {
        throw "PlayerProfile request failed. Uri=$ApiBaseUri, Error=$($_.Exception.Message)"
    }
}

function Confirm-AzureSmokePlayerProfile {
    param(
        [object]$Profile,
        [string]$ExpectedNickname
    )

    if ($null -eq $Profile -or [long]$Profile.id -le 0) {
        throw "PlayerProfile does not contain a valid player id."
    }

    if ([string]$Profile.nickname -ne $ExpectedNickname) {
        throw "PlayerProfile nickname does not match. Expected=$ExpectedNickname, Actual=$($Profile.nickname)"
    }

    if ([int]$Profile.gold -ne 1000 -or [int]$Profile.gem -ne 500) {
        throw "PlayerProfile initial currency does not match. Gold=$($Profile.gold), Gem=$($Profile.gem)"
    }

    foreach ($propertyName in @(
        "ownedCharacterCount",
        "partyCount",
        "clearedStageCount",
        "totalStageClearCount"
    )) {
        if ([int]$Profile.$propertyName -ne 0) {
            throw "New PlayerProfile count is not zero. Property=$propertyName, Value=$($Profile.$propertyName)"
        }
    }
}

function Confirm-PacketRange {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [int]$Count
    )

    if ($Offset -lt 0 -or
        $Count -lt 0 -or
        $Offset + $Count -gt $Buffer.Length) {
        throw "Packet does not contain the requested range. Offset=$Offset, Count=$Count, Length=$($Buffer.Length)"
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

    Confirm-PacketRange `
        -Buffer $Buffer `
        -Offset $Offset `
        -Count 2

    return [int]($Buffer[$Offset] -bor
        ($Buffer[$Offset + 1] -shl 8))
}

function Get-Int32LittleEndian {
    param(
        [byte[]]$Buffer,
        [int]$Offset
    )

    Confirm-PacketRange `
        -Buffer $Buffer `
        -Offset $Offset `
        -Count 4

    $valueBytes = [byte[]]::new(4)
    [Array]::Copy($Buffer, $Offset, $valueBytes, 0, 4)

    if (-not [BitConverter]::IsLittleEndian) {
        [Array]::Reverse($valueBytes)
    }

    return [BitConverter]::ToInt32($valueBytes, 0)
}

function Get-Int64LittleEndian {
    param(
        [byte[]]$Buffer,
        [int]$Offset
    )

    Confirm-PacketRange `
        -Buffer $Buffer `
        -Offset $Offset `
        -Count 8

    $valueBytes = [byte[]]::new(8)
    [Array]::Copy($Buffer, $Offset, $valueBytes, 0, 8)

    if (-not [BitConverter]::IsLittleEndian) {
        [Array]::Reverse($valueBytes)
    }

    return [BitConverter]::ToInt64($valueBytes, 0)
}

function New-AzureSmokeGamePacket {
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
        $read = $Stream.Read(
            $buffer,
            $offset,
            $Count - $offset)

        if ($read -eq 0) {
            throw "Server closed the connection while receiving a TCP packet. Received=$offset, Expected=$Count"
        }

        $offset += $read
    }

    return ,$buffer
}

function Read-AzureSmokeGamePacket {
    param([System.Net.Sockets.NetworkStream]$Stream)

    $sizeBytes = Read-Exactly -Stream $Stream -Count 2
    $packetSize = Get-UInt16LittleEndian `
        -Buffer $sizeBytes `
        -Offset 0

    if ($packetSize -lt 4 -or $packetSize -gt 4096) {
        throw "Server sent a TCP packet outside the allowed size range. Size=$packetSize"
    }

    $remaining = Read-Exactly `
        -Stream $Stream `
        -Count ($packetSize - 2)
    $packet = [byte[]]::new($packetSize)
    [Array]::Copy($sizeBytes, 0, $packet, 0, 2)
    [Array]::Copy($remaining, 0, $packet, 2, $remaining.Length)

    return ,$packet
}

function Send-AzureSmokeGamePacket {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [byte[]]$Packet
    )

    $Stream.Write($Packet, 0, $Packet.Length)
    $Stream.Flush()
}

function New-PacketReader {
    param(
        [byte[]]$Packet,
        [int]$ExpectedOpcode
    )

    if ($Packet.Length -lt 4 -or $Packet.Length -gt 4096) {
        throw "Packet length is outside the allowed range. Length=$($Packet.Length)"
    }

    $declaredSize = Get-UInt16LittleEndian `
        -Buffer $Packet `
        -Offset 0

    if ($declaredSize -ne $Packet.Length) {
        throw "Packet size header does not match the received length. Declared=$declaredSize, Actual=$($Packet.Length)"
    }

    $opcode = Get-UInt16LittleEndian `
        -Buffer $Packet `
        -Offset 2

    if ($opcode -ne $ExpectedOpcode) {
        throw "Unexpected TCP response opcode. Expected=$ExpectedOpcode, Actual=$opcode"
    }

    return [pscustomobject]@{
        Buffer = $Packet
        Offset = 4
    }
}

function Read-PacketBoolean {
    param([object]$Reader)

    Confirm-PacketRange `
        -Buffer $Reader.Buffer `
        -Offset $Reader.Offset `
        -Count 1

    $value = $Reader.Buffer[$Reader.Offset]
    $Reader.Offset++

    if ($value -notin @(0, 1)) {
        throw "Packet boolean value is invalid. Value=$value"
    }

    return $value -eq 1
}

function Read-PacketUInt16 {
    param([object]$Reader)

    $value = Get-UInt16LittleEndian `
        -Buffer $Reader.Buffer `
        -Offset $Reader.Offset
    $Reader.Offset += 2

    return $value
}

function Read-PacketInt32 {
    param([object]$Reader)

    $value = Get-Int32LittleEndian `
        -Buffer $Reader.Buffer `
        -Offset $Reader.Offset
    $Reader.Offset += 4

    return $value
}

function Read-PacketInt64 {
    param([object]$Reader)

    $value = Get-Int64LittleEndian `
        -Buffer $Reader.Buffer `
        -Offset $Reader.Offset
    $Reader.Offset += 8

    return $value
}

function Read-PacketString {
    param([object]$Reader)

    $byteCount = Read-PacketUInt16 -Reader $Reader
    Confirm-PacketRange `
        -Buffer $Reader.Buffer `
        -Offset $Reader.Offset `
        -Count $byteCount

    if ($byteCount -eq 0) {
        return [string]::Empty
    }

    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)

    try {
        $value = $strictUtf8.GetString(
            $Reader.Buffer,
            $Reader.Offset,
            $byteCount)
    }
    catch {
        throw "Packet string is not valid UTF-8. Error=$($_.Exception.Message)"
    }

    $Reader.Offset += $byteCount
    return $value
}

function Confirm-PacketFullyRead {
    param([object]$Reader)

    if ($Reader.Offset -ne $Reader.Buffer.Length) {
        throw "Packet contains unread payload. Offset=$($Reader.Offset), Length=$($Reader.Buffer.Length)"
    }
}

function ConvertFrom-AzureSmokeLoginPacket {
    param([byte[]]$Packet)

    $reader = New-PacketReader `
        -Packet $Packet `
        -ExpectedOpcode 2
    $result = [pscustomobject]@{
        Success = Read-PacketBoolean -Reader $reader
        Message = Read-PacketString -Reader $reader
    }
    Confirm-PacketFullyRead -Reader $reader

    return $result
}

function ConvertFrom-AzureSmokePlayerProfilePacket {
    param([byte[]]$Packet)

    $reader = New-PacketReader `
        -Packet $Packet `
        -ExpectedOpcode 16
    $result = [pscustomobject]@{
        Success = Read-PacketBoolean -Reader $reader
        Message = Read-PacketString -Reader $reader
        PlayerId = Read-PacketInt64 -Reader $reader
        Nickname = Read-PacketString -Reader $reader
        Gold = Read-PacketInt32 -Reader $reader
        Gem = Read-PacketInt32 -Reader $reader
        OwnedCharacterCount = Read-PacketInt32 -Reader $reader
        PartyCount = Read-PacketInt32 -Reader $reader
        ClearedStageCount = Read-PacketInt32 -Reader $reader
        TotalStageClearCount = Read-PacketInt32 -Reader $reader
    }
    Confirm-PacketFullyRead -Reader $reader

    return $result
}

function Connect-AzureSmokeTcpClient {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds
    )

    $asyncResult = $null

    try {
        $asyncResult = $Client.BeginConnect(
            $HostName,
            $Port,
            $null,
            $null)

        if (-not $asyncResult.AsyncWaitHandle.WaitOne(
                $TimeoutMilliseconds)) {
            throw "TCP connection timed out. Host=$HostName, Port=$Port"
        }

        $Client.EndConnect($asyncResult)
    }
    finally {
        if ($null -ne $asyncResult) {
            $asyncResult.AsyncWaitHandle.Close()
        }
    }
}

function Invoke-AzureSmokeGameTcpScenario {
    param(
        [string]$GameHost,
        [int]$GamePort,
        [string]$AccessToken
    )

    $tokenBytes = [Text.Encoding]::UTF8.GetBytes($AccessToken)

    if ($tokenBytes.Length -gt [UInt16]::MaxValue) {
        throw "Access token exceeds the TCP string length limit."
    }

    $tokenLengthBytes = New-UInt16LittleEndianBytes `
        -Value $tokenBytes.Length
    $loginPayload = [byte[]]::new(2 + $tokenBytes.Length)
    [Array]::Copy(
        $tokenLengthBytes,
        0,
        $loginPayload,
        0,
        2)
    [Array]::Copy(
        $tokenBytes,
        0,
        $loginPayload,
        2,
        $tokenBytes.Length)

    $client = [System.Net.Sockets.TcpClient]::new()
    $client.NoDelay = $true
    $client.ReceiveTimeout = 10000
    $client.SendTimeout = 10000

    try {
        Connect-AzureSmokeTcpClient `
            -Client $client `
            -HostName $GameHost `
            -Port $GamePort `
            -TimeoutMilliseconds 10000
        $stream = $client.GetStream()

        Send-AzureSmokeGamePacket `
            -Stream $stream `
            -Packet (New-AzureSmokeGamePacket `
                -Opcode 1 `
                -Payload $loginPayload)
        $loginResult = ConvertFrom-AzureSmokeLoginPacket `
            -Packet (Read-AzureSmokeGamePacket -Stream $stream)

        if (-not $loginResult.Success) {
            throw "TCP Login failed. Message=$($loginResult.Message)"
        }

        Send-AzureSmokeGamePacket `
            -Stream $stream `
            -Packet (New-AzureSmokeGamePacket -Opcode 15)
        $profileResult = ConvertFrom-AzureSmokePlayerProfilePacket `
            -Packet (Read-AzureSmokeGamePacket -Stream $stream)

        if (-not $profileResult.Success) {
            throw "TCP PlayerProfile failed. Message=$($profileResult.Message)"
        }

        return $profileResult
    }
    finally {
        $client.Dispose()
        [Array]::Clear($loginPayload, 0, $loginPayload.Length)
        [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
    }
}

function Get-AzureSmokeDeploymentPods {
    param(
        [object]$Context,
        [string]$DeploymentName
    )

    $deploymentJson = Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "get",
            "deployment/$DeploymentName",
            "--namespace",
            $Context.Namespace,
            "--output",
            "json"
        )
    $deployment = ConvertFrom-CommandJson `
        -Name "Deployment $DeploymentName" `
        -Json $deploymentJson
    $selectorLabels = @(
        $deployment.spec.selector.matchLabels.psobject.Properties)

    if ($selectorLabels.Count -eq 0) {
        throw "Deployment does not contain selector labels. Deployment=$DeploymentName"
    }

    $selector = ($selectorLabels |
        Sort-Object Name |
        ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ","
    $podsJson = Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "get",
            "pods",
            "--namespace",
            $Context.Namespace,
            "--selector",
            $selector,
            "--output",
            "json"
        )
    $pods = ConvertFrom-CommandJson `
        -Name "Pods for deployment $DeploymentName" `
        -Json $podsJson
    $podItems = @($pods.items)

    if ($podItems.Count -eq 0) {
        throw "Deployment does not have any Pods. Deployment=$DeploymentName"
    }

    return $podItems
}

function Test-AzureSmokePodReady {
    param([object]$Pod)

    $deletionTimestampProperty =
        $Pod.metadata.psobject.Properties["deletionTimestamp"]

    if ($null -ne $deletionTimestampProperty -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$deletionTimestampProperty.Value)) {
        return $false
    }

    $readyConditions = @($Pod.status.conditions |
        Where-Object { [string]$_.type -eq "Ready" })

    return $readyConditions.Count -eq 1 -and
        [string]$readyConditions[0].status -eq "True"
}

function Remove-AzureSmokeDeploymentPodAndWaitForReplacement {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
    param(
        [object]$Context,
        [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
        [string]$DeploymentName,
        [string]$PodName = "",
        [ValidateRange(1, 100)]
        [int]$ExpectedReplicas,
        [ValidateRange(1, 600)]
        [int]$TimeoutSeconds
    )

    $deployment = Confirm-DeploymentReady `
        -Context $Context `
        -DeploymentName $DeploymentName

    if ($deployment.Replicas -ne $ExpectedReplicas) {
        throw "Deployment replica count does not match the recovery test requirement. Deployment=$DeploymentName, Expected=$ExpectedReplicas, Actual=$($deployment.Replicas)"
    }

    $initialPods = @(Get-AzureSmokeDeploymentPods `
            -Context $Context `
            -DeploymentName $DeploymentName)

    if ($initialPods.Count -ne $ExpectedReplicas -or
        @($initialPods | Where-Object {
                Test-AzureSmokePodReady -Pod $_
            }).Count -ne $ExpectedReplicas) {
        throw "Recovery test requires every Deployment Pod to be Ready. Deployment=$DeploymentName, Expected=$ExpectedReplicas, Pods=$($initialPods.Count)"
    }

    $initialPodNames = @($initialPods |
        ForEach-Object { [string]$_.metadata.name })

    if ([string]::IsNullOrWhiteSpace($PodName)) {
        if ($ExpectedReplicas -ne 1) {
            throw "PodName is required for a multi-replica Deployment recovery test. Deployment=$DeploymentName, Replicas=$ExpectedReplicas"
        }

        $PodName = $initialPodNames[0]
    }
    elseif ($PodName -notmatch "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$") {
        throw "Recovery target Pod name is invalid. Pod=$PodName"
    }

    $targetPods = @($initialPods |
        Where-Object { [string]$_.metadata.name -eq $PodName })

    if ($targetPods.Count -ne 1) {
        throw "Recovery target Pod does not belong to the Deployment. Pod=$PodName, Deployment=$DeploymentName"
    }

    if (-not $PSCmdlet.ShouldProcess(
            "pod/$PodName in namespace $($Context.Namespace)",
            "Delete the Deployment Pod and wait for its replacement")) {
        return $null
    }

    Write-AzureSmokeStep `
        "Deleting Deployment Pod. Deployment=$DeploymentName, Pod=$PodName"
    Invoke-AzureSmokeKubectl `
        -Context $Context `
        -Arguments @(
            "delete",
            "pod/$PodName",
            "--namespace",
            $Context.Namespace,
            "--wait=false"
        ) | Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastObservation = "Replacement Pod has not been observed."

    do {
        try {
            $currentPods = @(Get-AzureSmokeDeploymentPods `
                    -Context $Context `
                    -DeploymentName $DeploymentName)
            $activePods = @($currentPods | Where-Object {
                    $deletionTimestampProperty =
                        $_.metadata.psobject.Properties["deletionTimestamp"]

                    $null -eq $deletionTimestampProperty -or
                    [string]::IsNullOrWhiteSpace(
                        [string]$deletionTimestampProperty.Value)
                })
            $readyPods = @($activePods | Where-Object {
                    Test-AzureSmokePodReady -Pod $_
                })
            $replacementPods = @($readyPods | Where-Object {
                    [string]$_.metadata.name -notin $initialPodNames
                })
            $deletedPodStillActive = @($activePods | Where-Object {
                    [string]$_.metadata.name -eq $PodName
                }).Count -gt 0

            $lastObservation =
                "Active=$($activePods.Count), Ready=$($readyPods.Count), Replacement=$($replacementPods.Count), DeletedPodStillActive=$deletedPodStillActive"

            if (-not $deletedPodStillActive -and
                $activePods.Count -eq $ExpectedReplicas -and
                $readyPods.Count -eq $ExpectedReplicas -and
                $replacementPods.Count -eq 1) {
                Confirm-DeploymentReady `
                    -Context $Context `
                    -DeploymentName $DeploymentName | Out-Null

                return [pscustomobject]@{
                    DeploymentName = $DeploymentName
                    DeletedPodName = $PodName
                    ReplacementPodName =
                        [string]$replacementPods[0].metadata.name
                    ReadyReplicas = $readyPods.Count
                }
            }
        }
        catch {
            $lastObservation = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Replacement Pod did not become ready. Deployment=$DeploymentName, DeletedPod=$PodName, TimeoutSeconds=$TimeoutSeconds, LastObservation=$lastObservation"
}

function Remove-AzureSmokeSiloPodAndWaitForReplacement {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
    param(
        [object]$Context,
        [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
        [string]$PodName,
        [ValidateRange(1, 600)]
        [int]$TimeoutSeconds
    )

    $deploymentName = "$($Context.ReleaseName)-silo"
    $target = "pod/$PodName in namespace $($Context.Namespace)"
    $action = "Delete the Silo Pod and wait for its replacement"

    if (-not $PSCmdlet.ShouldProcess($target, $action)) {
        return $null
    }

    return Remove-AzureSmokeDeploymentPodAndWaitForReplacement `
        -Context $Context `
        -DeploymentName $deploymentName `
        -PodName $PodName `
        -ExpectedReplicas 2 `
        -TimeoutSeconds $TimeoutSeconds `
        -Confirm:$false
}

function Get-AzureSmokePodLogText {
    param(
        [object]$Context,
        [string]$PodName,
        [string]$ContainerName,
        [Nullable[DateTimeOffset]]$SinceTime = $null
    )

    $arguments = @(
        "logs",
        "pod/$PodName",
        "--namespace",
        $Context.Namespace,
        "--container",
        $ContainerName
    )

    if ($null -ne $SinceTime) {
        $arguments += @(
            "--since-time",
            $SinceTime.ToUniversalTime().ToString("o")
        )
    }

    $kubectlArguments = @(
        "--context",
        $Context.KubernetesContext
    ) + $arguments
    $output = @(& $Context.KubectlPath @kubectlArguments 2>&1)
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        # 실패 출력에 인증 값이 포함될 가능성을 차단한 진단 정보 제한
        throw "kubectl logs failed. Pod=$PodName, Container=$ContainerName, ExitCode=$exitCode"
    }

    return ($output |
        ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
}

function Get-AzureSmokePodLogEntries {
    param(
        [object]$Context,
        [string]$PodName,
        [Nullable[DateTimeOffset]]$SinceTime = $null
    )

    $logText = Get-AzureSmokePodLogText `
        -Context $Context `
        -PodName $PodName `
        -ContainerName "silo" `
        -SinceTime $SinceTime

    foreach ($line in ($logText -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $entry = $line | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Silo log is not structured JSON. Pod=$PodName, Error=$($_.Exception.Message)"
        }

        [pscustomobject]@{
            PodName = $PodName
            Entry = $entry
        }
    }
}

function Confirm-AzureSmokeOrleansConfiguration {
    param([object]$Context)

    $deploymentName = "$($Context.ReleaseName)-silo"
    $pods = @(Get-AzureSmokeDeploymentPods `
            -Context $Context `
            -DeploymentName $deploymentName)

    foreach ($pod in $pods) {
        $podName = [string]$pod.metadata.name

        if (-not (Test-AzureSmokePodReady -Pod $pod)) {
            throw "Silo Pod is not Ready during Orleans configuration verification. Pod=$podName"
        }

        $runtimeText = Invoke-AzureSmokeKubectl `
            -Context $Context `
            -Arguments @(
                "exec",
                "pod/$podName",
                "--namespace",
                $Context.Namespace,
                "--container",
                "silo",
                "--",
                "printenv",
                "Orleans__HostingMode",
                "Orleans__ClusteringMode"
            )
        $runtimeValues = @($runtimeText -split "\r?\n" |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() })
        $hostingMode = if ($runtimeValues.Count -ge 1) {
            $runtimeValues[0]
        }
        else {
            "<missing>"
        }
        $clusteringMode = if ($runtimeValues.Count -ge 2) {
            $runtimeValues[1]
        }
        else {
            "<missing>"
        }

        if ($runtimeValues.Count -ne 2 -or
            $hostingMode -ne "Kubernetes" -or
            $clusteringMode -ne "Redis") {
            throw "Silo does not use the expected Orleans runtime configuration. Pod=$podName, HostingMode=$hostingMode, ClusteringMode=$clusteringMode"
        }
    }

    Write-AzureSmokeStep `
        "Confirmed Kubernetes hosting and Redis clustering runtime configuration on $($pods.Count) Silo Pods"
}

function Confirm-AzureSmokeRedisClusteringLog {
    param([object]$Context)

    Confirm-AzureSmokeOrleansConfiguration -Context $Context
}

function Wait-AzureSmokePlayerGrainActivation {
    param(
        [object]$Context,
        [long]$PlayerId,
        [DateTimeOffset]$SinceTime,
        [int]$TimeoutSeconds
    )

    $deploymentName = "$($Context.ReleaseName)-silo"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        $activationEntries = @()
        $pods = @(Get-AzureSmokeDeploymentPods `
                -Context $Context `
                -DeploymentName $deploymentName)

        foreach ($pod in $pods) {
            $podName = [string]$pod.metadata.name
            $entries = @(Get-AzureSmokePodLogEntries `
                    -Context $Context `
                    -PodName $podName `
                    -SinceTime $SinceTime)
            $activationEntries += @($entries | Where-Object {
                    [int]$_.Entry.EventId -eq 3000 -and
                    [long]$_.Entry.State.PlayerId -eq $PlayerId
                })
        }

        if ($activationEntries.Count -eq 1) {
            return [pscustomobject]@{
                PlayerId = $PlayerId
                PodName = $activationEntries[0].PodName
                ActivationCount = 1
            }
        }

        if ($activationEntries.Count -gt 1) {
            throw "PlayerProfile Grain was activated more than once during the scenario. PlayerId=$PlayerId, ActivationCount=$($activationEntries.Count)"
        }

        Start-Sleep -Milliseconds 500
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "PlayerProfile Grain activation log was not found. PlayerId=$PlayerId, TimeoutSeconds=$TimeoutSeconds"
}

function Get-AzureSmokeSensitiveLogFindings {
    param(
        [string]$LogText,
        [string]$WorkloadName,
        [string]$PodName,
        [System.Collections.IDictionary]$SensitiveValues
    )

    foreach ($sensitiveValue in $SensitiveValues.GetEnumerator()) {
        $value = [string]$sensitiveValue.Value

        if ($LogText.IndexOf(
                $value,
                [StringComparison]::Ordinal) -ge 0) {
            [pscustomobject]@{
                WorkloadName = $WorkloadName
                PodName = $PodName
                SecretKind = [string]$sensitiveValue.Key
            }
        }
    }
}

function Confirm-AzureSmokeSensitiveValuesNotLogged {
    param(
        [object]$Context,
        [DateTimeOffset]$SinceTime,
        [System.Collections.IDictionary]$SensitiveValues,
        [ValidateSet("API", "Game", "Silo")]
        [string[]]$WorkloadNames = @("API", "Game", "Silo")
    )

    foreach ($sensitiveValue in $SensitiveValues.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace(
                [string]$sensitiveValue.Value)) {
            throw "Sensitive value is missing. SecretKind=$($sensitiveValue.Key)"
        }
    }

    $availableWorkloads = @(
        [pscustomobject]@{
            Name = "API"
            DeploymentName = "$($Context.ReleaseName)-api"
            ContainerName = "api"
        },
        [pscustomobject]@{
            Name = "Game"
            DeploymentName = "$($Context.ReleaseName)-game"
            ContainerName = "game"
        },
        [pscustomobject]@{
            Name = "Silo"
            DeploymentName = "$($Context.ReleaseName)-silo"
            ContainerName = "silo"
        }
    )
    $workloads = @($availableWorkloads | Where-Object {
            $_.Name -in $WorkloadNames
        })
    $findings = @()

    foreach ($workload in $workloads) {
        $workloadLogCount = 0
        $pods = @(Get-AzureSmokeDeploymentPods `
                -Context $Context `
                -DeploymentName $workload.DeploymentName)

        foreach ($pod in $pods) {
            $podName = [string]$pod.metadata.name
            $logText = Get-AzureSmokePodLogText `
                -Context $Context `
                -PodName $podName `
                -ContainerName $workload.ContainerName `
                -SinceTime $SinceTime

            if ([string]::IsNullOrWhiteSpace($logText)) {
                continue
            }

            $workloadLogCount++
            $findings += @(Get-AzureSmokeSensitiveLogFindings `
                    -LogText $logText `
                    -WorkloadName $workload.Name `
                    -PodName $podName `
                    -SensitiveValues $SensitiveValues)
            $logText = $null
        }

        if ($workloadLogCount -eq 0) {
            throw "No logs were found for the Smoke Test time range. Workload=$($workload.Name)"
        }
    }

    if ($findings.Count -gt 0) {
        $locations = ($findings | ForEach-Object {
                "$($_.WorkloadName)/$($_.PodName):$($_.SecretKind)"
            }) -join ", "

        throw "Sensitive values were found in application logs. Locations=$locations"
    }

    Write-AzureSmokeStep "Confirmed Password and token values are absent from API, Game, and Silo logs"
}

function Confirm-AzureSmokeProfileMatch {
    param(
        [object]$HttpProfile,
        [object]$TcpProfile
    )

    $comparisons = @(
        @("PlayerId", [long]$HttpProfile.id, [long]$TcpProfile.PlayerId),
        @("Nickname", [string]$HttpProfile.nickname, [string]$TcpProfile.Nickname),
        @("Gold", [int]$HttpProfile.gold, [int]$TcpProfile.Gold),
        @("Gem", [int]$HttpProfile.gem, [int]$TcpProfile.Gem),
        @("OwnedCharacterCount", [int]$HttpProfile.ownedCharacterCount, [int]$TcpProfile.OwnedCharacterCount),
        @("PartyCount", [int]$HttpProfile.partyCount, [int]$TcpProfile.PartyCount),
        @("ClearedStageCount", [int]$HttpProfile.clearedStageCount, [int]$TcpProfile.ClearedStageCount),
        @("TotalStageClearCount", [int]$HttpProfile.totalStageClearCount, [int]$TcpProfile.TotalStageClearCount)
    )

    foreach ($comparison in $comparisons) {
        if ($comparison[1] -ne $comparison[2]) {
            throw "HTTP and TCP PlayerProfile values do not match. Field=$($comparison[0]), HTTP=$($comparison[1]), TCP=$($comparison[2])"
        }
    }
}

function Confirm-AzureSmokeHttpProfilesMatch {
    param(
        [object]$ExpectedProfile,
        [object]$ActualProfile
    )

    $comparisons = @(
        @("PlayerId", [long]$ExpectedProfile.id, [long]$ActualProfile.id),
        @("Nickname", [string]$ExpectedProfile.nickname, [string]$ActualProfile.nickname),
        @("Gold", [int]$ExpectedProfile.gold, [int]$ActualProfile.gold),
        @("Gem", [int]$ExpectedProfile.gem, [int]$ActualProfile.gem),
        @("OwnedCharacterCount", [int]$ExpectedProfile.ownedCharacterCount, [int]$ActualProfile.ownedCharacterCount),
        @("PartyCount", [int]$ExpectedProfile.partyCount, [int]$ActualProfile.partyCount),
        @("ClearedStageCount", [int]$ExpectedProfile.clearedStageCount, [int]$ActualProfile.clearedStageCount),
        @("TotalStageClearCount", [int]$ExpectedProfile.totalStageClearCount, [int]$ActualProfile.totalStageClearCount)
    )

    foreach ($comparison in $comparisons) {
        if ($comparison[1] -ne $comparison[2]) {
            throw "HTTP PlayerProfile values do not match. Field=$($comparison[0]), Expected=$($comparison[1]), Actual=$($comparison[2])"
        }
    }
}

Export-ModuleMember -Function @(
    "Write-AzureSmokeStep",
    "New-AzureSmokeContext",
    "Confirm-AzureSmokeDeployment",
    "Start-AzureSmokePortForward",
    "Wait-AzureSmokePortForward",
    "Stop-AzureSmokePortForward",
    "New-AzureSmokePlayerSession",
    "Clear-AzureSmokePlayerSession",
    "Get-AzureSmokePlayerProfile",
    "Confirm-AzureSmokePlayerProfile",
    "Invoke-AzureSmokeGameTcpScenario",
    "Confirm-AzureSmokeProfileMatch",
    "Confirm-AzureSmokeHttpProfilesMatch",
    "Confirm-AzureSmokeOrleansConfiguration",
    "Confirm-AzureSmokeRedisClusteringLog",
    "Wait-AzureSmokePlayerGrainActivation",
    "Confirm-AzureSmokeSensitiveValuesNotLogged",
    "Remove-AzureSmokeDeploymentPodAndWaitForReplacement",
    "Remove-AzureSmokeSiloPodAndWaitForReplacement"
)
