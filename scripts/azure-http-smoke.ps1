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
$playerSession = $null

try {
    $deployment = Confirm-AzureSmokeDeployment `
        -Context $context

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

    $apiBaseUri = "http://127.0.0.1:$ApiLocalPort"
    $playerSession = New-AzureSmokePlayerSession `
        -ApiBaseUri $apiBaseUri

    Write-AzureSmokeStep "Requesting PlayerProfile with JWT"
    $profile = Get-AzureSmokePlayerProfile `
        -ApiBaseUri $apiBaseUri `
        -AccessToken $playerSession.AccessToken
    Confirm-AzureSmokePlayerProfile `
        -Profile $profile `
        -ExpectedNickname $playerSession.Nickname

    Write-AzureSmokeStep "Success: Azure HTTP Smoke Test completed"
    Write-Host "[azure-smoke] HelmRevision=$($deployment.HelmRevision), ImageTag=$($deployment.ImageTag), PlayerId=$($profile.id), Nickname=$($playerSession.Nickname)"
}
finally {
    # 인증 값과 Background Process 정리
    Clear-AzureSmokePlayerSession -Session $playerSession

    Stop-AzureSmokePortForward -Process $apiPortForward
}
