[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$KubernetesContext = "aks-blue-server-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$Namespace = "blue-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$ReleaseName = "blue-server",

    [ValidateRange(1, 65535)]
    [int]$ApiLocalPort = 15201,

    [ValidateRange(1, 65535)]
    [int]$GameLocalPort = 17777,

    [ValidateRange(1, 300)]
    [int]$PortForwardStartupTimeoutSeconds = 30,

    [ValidateRange(1, 600)]
    [int]$GameReplacementTimeoutSeconds = 180,

    [ValidateRange(1, 60)]
    [int]$ConnectionShutdownTimeoutSeconds = 15,

    [ValidateRange(1, 600)]
    [int]$ApplicationRecoveryTimeoutSeconds = 120,

    [ValidateRange(1, 60)]
    [int]$RecoveryRetryIntervalSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ApiLocalPort -eq $GameLocalPort) {
    throw "API and Game local ports must be different. Port=$ApiLocalPort"
}

$moduleDirectory = Join-Path $PSScriptRoot "smoke"
$modulePath = Join-Path `
    $moduleDirectory `
    "BlueServer.AzureSmoke.psm1"
Import-Module $modulePath -Force

function New-AzureSmokeTcpProbeConnection {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutSeconds
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    $client.NoDelay = $true
    $asyncResult = $null

    try {
        $asyncResult = $client.BeginConnect(
            $HostName,
            $Port,
            $null,
            $null)

        if (-not $asyncResult.AsyncWaitHandle.WaitOne(
                [TimeSpan]::FromSeconds($TimeoutSeconds))) {
            throw "TCP probe connection timed out. Host=$HostName, Port=$Port, TimeoutSeconds=$TimeoutSeconds"
        }

        $client.EndConnect($asyncResult)
        return $client
    }
    catch {
        $client.Dispose()
        throw
    }
    finally {
        if ($null -ne $asyncResult) {
            $asyncResult.AsyncWaitHandle.Close()
        }
    }
}

function Test-AzureSmokeTcpConnectionClosed {
    param([System.Net.Sockets.TcpClient]$Client)

    try {
        return $Client.Client.Poll(
            1000,
            [System.Net.Sockets.SelectMode]::SelectRead) -and
            $Client.Client.Available -eq 0
    }
    catch {
        return $true
    }
}

function Wait-AzureSmokeGameConnectionClosed {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        if (Test-AzureSmokeTcpConnectionClosed -Client $Client) {
            return [pscustomobject]@{
                ConnectionClosed = $true
            }
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Existing Game TCP connection did not close after Pod deletion. TimeoutSeconds=$TimeoutSeconds"
}

function Invoke-AzureSmokeTcpProfileRecovery {
    param(
        [int]$GamePort,
        [string]$AccessToken,
        [object]$BaselineHttpProfile,
        [int]$TimeoutSeconds,
        [int]$RetryIntervalSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempt = 0
    $lastError = "Recovery request has not been attempted."

    do {
        $attempt++

        try {
            $profile = Invoke-AzureSmokeGameTcpScenario `
                -GameHost "127.0.0.1" `
                -GamePort $GamePort `
                -AccessToken $AccessToken
            Confirm-AzureSmokeProfileMatch `
                -HttpProfile $BaselineHttpProfile `
                -TcpProfile $profile

            return [pscustomobject]@{
                Profile = $profile
                AttemptCount = $attempt
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Write-AzureSmokeStep `
                "TCP Login and PlayerProfile recovery request is not ready. Attempt=$attempt"
        }

        Start-Sleep -Seconds $RetryIntervalSeconds
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "TCP flow did not recover after Game Pod deletion. Attempts=$attempt, TimeoutSeconds=$TimeoutSeconds, LastError=$lastError"
}

$context = New-AzureSmokeContext `
    -KubernetesContext $KubernetesContext `
    -Namespace $Namespace `
    -ReleaseName $ReleaseName
$apiPortForward = $null
$gamePortForward = $null
$gameProbeConnection = $null
$playerSession = $null

try {
    $deployment = Confirm-AzureSmokeDeployment `
        -Context $context

    if ($deployment.GameReplicas -ne 1) {
        throw "Game recovery Smoke Test requires exactly one Game replica. Actual=$($deployment.GameReplicas)"
    }

    Write-AzureSmokeStep "Checking Orleans Silo runtime configuration"
    Confirm-AzureSmokeOrleansConfiguration -Context $context

    Write-AzureSmokeStep "Starting private API port-forward"
    $apiPortForward = Start-AzureSmokePortForward `
        -Context $context `
        -ServiceName "$ReleaseName-api" `
        -LocalPort $ApiLocalPort `
        -RemotePort 80
    Wait-AzureSmokePortForward `
        -Process $apiPortForward `
        -LocalPort $ApiLocalPort `
        -StartupTimeoutSeconds $PortForwardStartupTimeoutSeconds

    Write-AzureSmokeStep "Starting private Game port-forward"
    $gamePortForward = Start-AzureSmokePortForward `
        -Context $context `
        -ServiceName "$ReleaseName-game" `
        -LocalPort $GameLocalPort `
        -RemotePort 7777
    Wait-AzureSmokePortForward `
        -Process $gamePortForward `
        -LocalPort $GameLocalPort `
        -StartupTimeoutSeconds $PortForwardStartupTimeoutSeconds

    # 장애 전 Player와 HTTP·TCP Profile 기준값 생성
    $scenarioStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $apiBaseUri = "http://127.0.0.1:$ApiLocalPort"
    $playerSession = New-AzureSmokePlayerSession `
        -ApiBaseUri $apiBaseUri
    $baselineHttpProfile = Get-AzureSmokePlayerProfile `
        -ApiBaseUri $apiBaseUri `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokePlayerProfile `
        -Profile $baselineHttpProfile `
        -ExpectedNickname $playerSession.Nickname
    $baselineTcpProfile = Invoke-AzureSmokeGameTcpScenario `
        -GameHost "127.0.0.1" `
        -GamePort $GameLocalPort `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokeProfileMatch `
        -HttpProfile $baselineHttpProfile `
        -TcpProfile $baselineTcpProfile

    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $scenarioStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        })

    # Pod 삭제 시 기존 TCP 경로 종료를 확인하기 위한 유휴 연결 유지
    $gameProbeConnection = New-AzureSmokeTcpProbeConnection `
        -HostName "127.0.0.1" `
        -Port $GameLocalPort `
        -TimeoutSeconds $PortForwardStartupTimeoutSeconds

    if (Test-AzureSmokeTcpConnectionClosed `
            -Client $gameProbeConnection) {
        throw "Game TCP probe connection closed before Pod deletion."
    }

    $gamePortForward.Refresh()

    if ($gamePortForward.HasExited) {
        throw "Game Port Forward exited before Pod deletion."
    }

    $failureStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $replacement = Remove-AzureSmokeDeploymentPodAndWaitForReplacement `
        -Context $context `
        -DeploymentName "$ReleaseName-game" `
        -ExpectedReplicas 1 `
        -TimeoutSeconds $GameReplacementTimeoutSeconds

    if ($null -eq $replacement) {
        Write-AzureSmokeStep `
            "Game Pod deletion was not approved. Recovery test skipped."
        return
    }

    Write-AzureSmokeStep `
        "Confirmed replacement Game Pod readiness. Pod=$($replacement.ReplacementPodName)"
    $transport = Wait-AzureSmokeGameConnectionClosed `
        -Client $gameProbeConnection `
        -TimeoutSeconds $ConnectionShutdownTimeoutSeconds
    $gamePortForward.Refresh()
    $portForwardExitedBeforeCleanup = $gamePortForward.HasExited
    Write-AzureSmokeStep `
        "Confirmed existing TCP connection termination. PortForwardExitedBeforeCleanup=$portForwardExitedBeforeCleanup"

    $gameProbeConnection.Dispose()
    $gameProbeConnection = $null
    Stop-AzureSmokePortForward -Process $gamePortForward
    $gamePortForward = $null

    # 교체 Pod를 대상으로 새 Game Port Forward 연결
    Write-AzureSmokeStep "Restarting private Game port-forward"
    $gamePortForward = Start-AzureSmokePortForward `
        -Context $context `
        -ServiceName "$ReleaseName-game" `
        -LocalPort $GameLocalPort `
        -RemotePort 7777
    Wait-AzureSmokePortForward `
        -Process $gamePortForward `
        -LocalPort $GameLocalPort `
        -StartupTimeoutSeconds $PortForwardStartupTimeoutSeconds

    $recovery = Invoke-AzureSmokeTcpProfileRecovery `
        -GamePort $GameLocalPort `
        -AccessToken $playerSession.AccessToken `
        -BaselineHttpProfile $baselineHttpProfile `
        -TimeoutSeconds $ApplicationRecoveryTimeoutSeconds `
        -RetryIntervalSeconds $RecoveryRetryIntervalSeconds

    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $failureStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        }) `
        -WorkloadNames @("Game")

    Write-AzureSmokeStep `
        "Success: Azure Game recovery Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($deployment.HelmRevision), ImageTag=$($deployment.ImageTag), PlayerId=$($recovery.Profile.PlayerId), DeletedGamePod=$($replacement.DeletedPodName), ReplacementGamePod=$($replacement.ReplacementPodName), ExistingConnectionClosed=$($transport.ConnectionClosed), PreviousPortForwardExited=$portForwardExitedBeforeCleanup, RecoveryAttempts=$($recovery.AttemptCount)"
}
finally {
    # 인증 값과 Socket·Background Process 정리
    Clear-AzureSmokePlayerSession -Session $playerSession

    if ($null -ne $gameProbeConnection) {
        $gameProbeConnection.Dispose()
    }

    Stop-AzureSmokePortForward -Process $gamePortForward
    Stop-AzureSmokePortForward -Process $apiPortForward
}
