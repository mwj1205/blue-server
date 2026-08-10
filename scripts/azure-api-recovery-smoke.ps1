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

    [ValidateRange(1, 300)]
    [int]$PortForwardStartupTimeoutSeconds = 30,

    [ValidateRange(1, 600)]
    [int]$ApiReplacementTimeoutSeconds = 180,

    [ValidateRange(1, 600)]
    [int]$ApplicationRecoveryTimeoutSeconds = 120,

    [ValidateRange(1, 60)]
    [int]$RecoveryRetryIntervalSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$moduleDirectory = Join-Path $PSScriptRoot "smoke"
$modulePath = Join-Path `
    $moduleDirectory `
    "BlueServer.AzureSmoke.psm1"
Import-Module $modulePath -Force

function Invoke-AzureSmokeHttpProfileRecovery {
    param(
        [string]$ApiBaseUri,
        [object]$Session,
        [object]$BaselineProfile,
        [int]$TimeoutSeconds,
        [int]$RetryIntervalSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempt = 0
    $lastError = "Recovery request has not been attempted."

    do {
        $attempt++

        try {
            $profile = Get-AzureSmokePlayerProfile `
                -ApiBaseUri $ApiBaseUri `
                -AccessToken $Session.AccessToken
            Confirm-AzureSmokePlayerProfile `
                -Profile $profile `
                -ExpectedNickname $Session.Nickname
            Confirm-AzureSmokeHttpProfilesMatch `
                -ExpectedProfile $BaselineProfile `
                -ActualProfile $profile

            return [pscustomobject]@{
                Profile = $profile
                AttemptCount = $attempt
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Write-AzureSmokeStep `
                "HTTP PlayerProfile recovery request is not ready. Attempt=$attempt"
        }

        Start-Sleep -Seconds $RetryIntervalSeconds
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "HTTP PlayerProfile did not recover after API Pod deletion. Attempts=$attempt, TimeoutSeconds=$TimeoutSeconds, LastError=$lastError"
}

$context = New-AzureSmokeContext `
    -KubernetesContext $KubernetesContext `
    -Namespace $Namespace `
    -ReleaseName $ReleaseName
$apiPortForward = $null
$playerSession = $null

try {
    $deployment = Confirm-AzureSmokeDeployment `
        -Context $context

    if ($deployment.ApiReplicas -ne 1) {
        throw "API recovery Smoke Test requires exactly one API replica. Actual=$($deployment.ApiReplicas)"
    }

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

    # 장애 전 기준 Player와 HTTP Profile 생성
    $scenarioStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $apiBaseUri = "http://127.0.0.1:$ApiLocalPort"
    $playerSession = New-AzureSmokePlayerSession `
        -ApiBaseUri $apiBaseUri
    $baselineProfile = Get-AzureSmokePlayerProfile `
        -ApiBaseUri $apiBaseUri `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokePlayerProfile `
        -Profile $baselineProfile `
        -ExpectedNickname $playerSession.Nickname

    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $scenarioStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        }) `
        -WorkloadNames @("API")

    # Port Forward가 기존 Pod에 고정되므로 삭제 전에 연결 종료
    Stop-AzureSmokePortForward -Process $apiPortForward
    $apiPortForward = $null

    $failureStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $replacement = Remove-AzureSmokeDeploymentPodAndWaitForReplacement `
        -Context $context `
        -DeploymentName "$ReleaseName-api" `
        -ExpectedReplicas 1 `
        -TimeoutSeconds $ApiReplacementTimeoutSeconds

    if ($null -eq $replacement) {
        Write-AzureSmokeStep `
            "API Pod deletion was not approved. Recovery test skipped."
        return
    }

    Write-AzureSmokeStep `
        "Confirmed replacement API Pod readiness. Pod=$($replacement.ReplacementPodName)"

    # 교체 Pod를 대상으로 새 Port Forward 연결
    Write-AzureSmokeStep "Restarting private API port-forward"
    $apiPortForward = Start-AzureSmokePortForward `
        -Context $context `
        -ServiceName "$ReleaseName-api" `
        -LocalPort $ApiLocalPort `
        -RemotePort 80
    Wait-AzureSmokePortForward `
        -Process $apiPortForward `
        -LocalPort $ApiLocalPort `
        -StartupTimeoutSeconds $PortForwardStartupTimeoutSeconds

    $recovery = Invoke-AzureSmokeHttpProfileRecovery `
        -ApiBaseUri $apiBaseUri `
        -Session $playerSession `
        -BaselineProfile $baselineProfile `
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
        -WorkloadNames @("API")

    Write-AzureSmokeStep `
        "Success: Azure API recovery Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($deployment.HelmRevision), ImageTag=$($deployment.ImageTag), PlayerId=$($recovery.Profile.id), DeletedApiPod=$($replacement.DeletedPodName), ReplacementApiPod=$($replacement.ReplacementPodName), RecoveryAttempts=$($recovery.AttemptCount)"
}
finally {
    # 인증 값과 Background Process 정리
    Clear-AzureSmokePlayerSession -Session $playerSession

    Stop-AzureSmokePortForward -Process $apiPortForward
}
