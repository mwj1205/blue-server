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

    [ValidateRange(1, 65535)]
    [int]$GameLocalPort = 7777,

    [ValidateRange(1, 300)]
    [int]$PortForwardStartupTimeoutSeconds = 30,

    [ValidateRange(1, 60)]
    [int]$GrainActivationTimeoutSeconds = 15
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

    # 신규 Player의 Grain Activation만 추적하기 위한 시나리오 시작 시각
    $scenarioStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $apiBaseUri = "http://127.0.0.1:$ApiLocalPort"
    $playerSession = New-AzureSmokePlayerSession `
        -ApiBaseUri $apiBaseUri

    Write-AzureSmokeStep "Requesting HTTP PlayerProfile for comparison"
    $httpProfile = Get-AzureSmokePlayerProfile `
        -ApiBaseUri $apiBaseUri `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokePlayerProfile `
        -Profile $httpProfile `
        -ExpectedNickname $playerSession.Nickname

    Write-AzureSmokeStep "Requesting TCP Login and PlayerProfile"
    $tcpProfile = Invoke-AzureSmokeGameTcpScenario `
        -GameHost "127.0.0.1" `
        -GamePort $GameLocalPort `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokeProfileMatch `
        -HttpProfile $httpProfile `
        -TcpProfile $tcpProfile

    Write-AzureSmokeStep "Checking single PlayerProfile Grain activation"
    $grainActivation = Wait-AzureSmokePlayerGrainActivation `
        -Context $context `
        -PlayerId $tcpProfile.PlayerId `
        -SinceTime $scenarioStartedAt `
        -TimeoutSeconds $GrainActivationTimeoutSeconds

    Write-AzureSmokeStep "Checking application logs for sensitive values"
    Confirm-AzureSmokeSensitiveValuesNotLogged `
        -Context $context `
        -SinceTime $scenarioStartedAt `
        -SensitiveValues ([ordered]@{
            Password = $playerSession.Password
            AccessToken = $playerSession.AccessToken
            RefreshToken = $playerSession.RefreshToken
        })

    Write-AzureSmokeStep "Success: Azure HTTP, TCP, and Orleans Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($deployment.HelmRevision), ImageTag=$($deployment.ImageTag), PlayerId=$($tcpProfile.PlayerId), Nickname=$($tcpProfile.Nickname), Gold=$($tcpProfile.Gold), Gem=$($tcpProfile.Gem), OwnedCharacters=$($tcpProfile.OwnedCharacterCount), Parties=$($tcpProfile.PartyCount), ClearedStages=$($tcpProfile.ClearedStageCount), TotalStageClears=$($tcpProfile.TotalStageClearCount), GrainPod=$($grainActivation.PodName), GrainActivations=$($grainActivation.ActivationCount)"
}
finally {
    # 인증 값과 Background Process 정리
    Clear-AzureSmokePlayerSession -Session $playerSession

    Stop-AzureSmokePortForward -Process $gamePortForward
    Stop-AzureSmokePortForward -Process $apiPortForward
}
