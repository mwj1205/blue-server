[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$KubernetesContext = "aks-blue-server-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$Namespace = "blue-dev",

    [ValidatePattern("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    [string]$ReleaseName = "blue-server",

    [ValidateRange(1, 65535)]
    [int]$ApiLocalPort = 5201,

    [ValidateRange(1, 65535)]
    [int]$GameLocalPort = 7777,

    [ValidateRange(1, 300)]
    [int]$PortForwardStartupTimeoutSeconds = 30,

    [ValidateRange(1, 60)]
    [int]$GrainActivationTimeoutSeconds = 15,

    [ValidateRange(1, 600)]
    [int]$SiloReplacementTimeoutSeconds = 180,

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

function Invoke-AzureSmokeProfileRecovery {
    param(
        [string]$ApiBaseUri,
        [int]$GamePort,
        [object]$Session,
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
            $httpProfile = Get-AzureSmokePlayerProfile `
                -ApiBaseUri $ApiBaseUri `
                -AccessToken $Session.AccessToken
            Confirm-AzureSmokePlayerProfile `
                -Profile $httpProfile `
                -ExpectedNickname $Session.Nickname
            $tcpProfile = Invoke-AzureSmokeGameTcpScenario `
                -GameHost "127.0.0.1" `
                -GamePort $GamePort `
                -AccessToken $Session.AccessToken

            # 장애 전후 Profile과 복구 후 HTTP·TCP 응답의 정합성 검증
            Confirm-AzureSmokeProfileMatch `
                -HttpProfile $BaselineHttpProfile `
                -TcpProfile $tcpProfile
            Confirm-AzureSmokeProfileMatch `
                -HttpProfile $httpProfile `
                -TcpProfile $tcpProfile

            return [pscustomobject]@{
                HttpProfile = $httpProfile
                TcpProfile = $tcpProfile
                AttemptCount = $attempt
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Write-AzureSmokeStep `
                "PlayerProfile recovery request is not ready. Attempt=$attempt"
        }

        Start-Sleep -Seconds $RetryIntervalSeconds
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "PlayerProfile did not recover after Silo Pod deletion. Attempts=$attempt, TimeoutSeconds=$TimeoutSeconds, LastError=$lastError"
}

$context = New-AzureSmokeContext `
    -KubernetesContext $KubernetesContext `
    -Namespace $Namespace `
    -ReleaseName $ReleaseName
$apiPortForward = $null
$gamePortForward = $null
$playerSession = $null

try {
    $deployment = Confirm-AzureSmokeDeployment `
        -Context $context

    Write-AzureSmokeStep "Checking Orleans Silo clustering configuration logs"
    Confirm-AzureSmokeRedisClusteringLog -Context $context

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

    # 장애 전 기준 Player와 Grain Activation 생성
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
    $baselineActivation = Wait-AzureSmokePlayerGrainActivation `
        -Context $context `
        -PlayerId $baselineTcpProfile.PlayerId `
        -SinceTime $scenarioStartedAt `
        -TimeoutSeconds $GrainActivationTimeoutSeconds

    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $scenarioStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        })

    $podTarget = "pod/$($baselineActivation.PodName) in namespace $Namespace"
    $podAction = "Delete the Silo Pod that owns PlayerProfile Grain $($baselineTcpProfile.PlayerId)"

    if (-not $PSCmdlet.ShouldProcess($podTarget, $podAction)) {
        Write-AzureSmokeStep "Silo Pod deletion was not approved. Recovery test skipped."
        return
    }

    # 장애 발생 시각 이후의 재활성화 Log만 추적
    $failureStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $replacement = Remove-AzureSmokeSiloPodAndWaitForReplacement `
        -Context $context `
        -PodName $baselineActivation.PodName `
        -TimeoutSeconds $SiloReplacementTimeoutSeconds `
        -Confirm:$false

    Write-AzureSmokeStep `
        "Confirmed replacement Silo Pod readiness. Pod=$($replacement.ReplacementPodName)"
    Confirm-AzureSmokeRedisClusteringLog -Context $context

    # Orleans Client membership 갱신 동안 제한된 재시도 적용
    $recovery = Invoke-AzureSmokeProfileRecovery `
        -ApiBaseUri $apiBaseUri `
        -GamePort $GameLocalPort `
        -Session $playerSession `
        -BaselineHttpProfile $baselineHttpProfile `
        -TimeoutSeconds $ApplicationRecoveryTimeoutSeconds `
        -RetryIntervalSeconds $RecoveryRetryIntervalSeconds
    $recoveryActivation = Wait-AzureSmokePlayerGrainActivation `
        -Context $context `
        -PlayerId $recovery.TcpProfile.PlayerId `
        -SinceTime $failureStartedAt `
        -TimeoutSeconds $GrainActivationTimeoutSeconds

    if ($recoveryActivation.PodName -eq $baselineActivation.PodName) {
        throw "PlayerProfile Grain was reported on the deleted Silo Pod. Pod=$($recoveryActivation.PodName)"
    }

    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $failureStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        })

    Write-AzureSmokeStep `
        "Success: Azure Silo recovery Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($deployment.HelmRevision), ImageTag=$($deployment.ImageTag), PlayerId=$($recovery.TcpProfile.PlayerId), DeletedSiloPod=$($replacement.DeletedPodName), ReplacementSiloPod=$($replacement.ReplacementPodName), ReactivatedGrainPod=$($recoveryActivation.PodName), RecoveryAttempts=$($recovery.AttemptCount)"
}
finally {
    # 인증 값과 Background Process 정리
    Clear-AzureSmokePlayerSession -Session $playerSession

    Stop-AzureSmokePortForward -Process $gamePortForward
    Stop-AzureSmokePortForward -Process $apiPortForward
}
