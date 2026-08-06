[CmdletBinding()]
param(
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$KubernetesContext = "aks-blue-server-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$Namespace = "blue-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$ReleaseName = "blue-server",

    [ValidateRange(1, 65535)]
    [int]$ApiLocalPort = 5201,

    [ValidateRange(1, 300)]
    [int]$PortForwardStartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
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

function Invoke-Kubectl {
    param([string[]]$Arguments)

    return Invoke-NativeCommand `
        -FilePath $script:KubectlPath `
        -Arguments (@("--context", $KubernetesContext) + $Arguments)
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

function Assert-HelmRelease {
    $statusJson = Invoke-NativeCommand `
        -FilePath $script:HelmPath `
        -Arguments @(
            "status",
            $ReleaseName,
            "--namespace",
            $Namespace,
            "--kube-context",
            $KubernetesContext,
            "--output",
            "json"
        )
    $status = ConvertFrom-CommandJson `
        -Name "Helm release $ReleaseName" `
        -Json $statusJson

    if ($status.info.status -ne "deployed") {
        throw "Helm release is not deployed. Release=$ReleaseName, Status=$($status.info.status)"
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

function Assert-DeploymentReady {
    param([string]$DeploymentName)

    $deploymentJson = Invoke-Kubectl `
        -Arguments @(
            "get",
            "deployment/$DeploymentName",
            "--namespace",
            $Namespace,
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

function Assert-ClusterIpService {
    param(
        [string]$ServiceName,
        [int]$ExpectedPort
    )

    $serviceJson = Invoke-Kubectl `
        -Arguments @(
            "get",
            "service/$ServiceName",
            "--namespace",
            $Namespace,
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

function Assert-LocalPortAvailable {
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

function Start-KubectlPortForward {
    param(
        [string]$ServiceName,
        [int]$LocalPort,
        [int]$RemotePort
    )

    Assert-LocalPortAvailable -Port $LocalPort

    # Public Service 생성 없이 ClusterIP에 임시 연결
    $arguments = @(
        "--context",
        $KubernetesContext,
        "port-forward",
        "service/$ServiceName",
        "${LocalPort}:${RemotePort}",
        "--namespace",
        $Namespace,
        "--address",
        "127.0.0.1"
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:KubectlPath
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

function Wait-PortForwardReady {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$LocalPort
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(
        $PortForwardStartupTimeoutSeconds)

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

    throw "kubectl port-forward did not become ready. LocalPort=$LocalPort, TimeoutSeconds=$PortForwardStartupTimeoutSeconds"
}

function Stop-PortForward {
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

function Assert-PlayerProfile {
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

$kubectlCommand = Get-Command kubectl -ErrorAction Stop
$helmCommand = Get-Command helm -ErrorAction Stop
$script:KubectlPath = $kubectlCommand.Source
$script:HelmPath = $helmCommand.Source

$apiPortForward = $null
$password = $null
$accessToken = $null
$loginResponse = $null

try {
    Write-Step "Checking AKS context and namespace"
    Invoke-Kubectl `
        -Arguments @(
            "get",
            "namespace/$Namespace",
            "--output",
            "name"
        ) | Out-Null

    Write-Step "Checking Helm release"
    $release = Assert-HelmRelease

    Write-Step "Checking API, Game, and Silo deployments"
    $apiDeployment = Assert-DeploymentReady `
        -DeploymentName "$ReleaseName-api"
    $gameDeployment = Assert-DeploymentReady `
        -DeploymentName "$ReleaseName-game"
    $siloDeployment = Assert-DeploymentReady `
        -DeploymentName "$ReleaseName-silo"

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

    Write-Step "Checking private ClusterIP services"
    Assert-ClusterIpService `
        -ServiceName "$ReleaseName-api" `
        -ExpectedPort 80
    Assert-ClusterIpService `
        -ServiceName "$ReleaseName-game" `
        -ExpectedPort 7777

    Write-Step "Starting private API port-forward"
    $apiPortForward = Start-KubectlPortForward `
        -ServiceName "$ReleaseName-api" `
        -LocalPort $ApiLocalPort `
        -RemotePort 80
    Wait-PortForwardReady `
        -Process $apiPortForward `
        -LocalPort $ApiLocalPort

    $apiBaseUri = "http://127.0.0.1:$ApiLocalPort"
    $nickname = "smoke-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))-$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
    $password = "$([Guid]::NewGuid().ToString('N'))aA1!"

    Write-Step "Registering a temporary Player"
    Invoke-JsonPost `
        -Name "Register" `
        -Uri "$apiBaseUri/register" `
        -Body @{
            nickname = $nickname
            password = $password
        } | Out-Null

    Write-Step "Logging in with the temporary Player"
    $loginResponse = Invoke-JsonPost `
        -Name "Login" `
        -Uri "$apiBaseUri/login" `
        -Body @{
            nickname = $nickname
            password = $password
        }
    $accessToken = [string]$loginResponse.accessToken
    $loginResponse = $null

    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw "Login response does not contain an access token."
    }

    Write-Step "Requesting PlayerProfile with JWT"
    $profile = Invoke-AuthorizedJsonGet `
        -Name "PlayerProfile" `
        -Uri "$apiBaseUri/players/me/profile" `
        -AccessToken $accessToken
    Assert-PlayerProfile `
        -Profile $profile `
        -ExpectedNickname $nickname

    Write-Step "Success: Azure HTTP Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($release.Revision), ImageTag=$($imageTags[0]), PlayerId=$($profile.id), Nickname=$nickname"
}
finally {
    # 인증 값과 Background Process 정리
    $loginResponse = $null
    $accessToken = $null
    $password = $null
    Stop-PortForward -Process $apiPortForward
}
