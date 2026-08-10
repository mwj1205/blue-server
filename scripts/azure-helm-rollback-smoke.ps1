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

    [ValidateRange(30, 600)]
    [int]$FailedUpgradeTimeoutSeconds = 90,

    [ValidateRange(30, 600)]
    [int]$RollbackTimeoutSeconds = 300,

    [switch]$AllowNonDevelopmentNamespace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ApiLocalPort -eq $GameLocalPort) {
    throw "API and Game local ports must be different. Port=$ApiLocalPort"
}

if (-not $AllowNonDevelopmentNamespace -and
    $Namespace -notmatch "(^|-)dev($|-)") {
    throw "Helm rollback Smoke Test is restricted to a development Namespace. Namespace=$Namespace"
}

$moduleDirectory = Join-Path $PSScriptRoot "smoke"
$modulePath = Join-Path `
    $moduleDirectory `
    "BlueServer.AzureSmoke.psm1"
Import-Module $modulePath -Force

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$chartPath = Join-Path `
    $repositoryRoot `
    "deploy\helm\blue-server"
$integratedSmokePath = Join-Path `
    $PSScriptRoot `
    "azure-tcp-smoke.ps1"

function Invoke-AzureSmokeHelmCommand {
    param(
        [object]$Context,
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference

    try {
        # Helm의 예상된 stderr를 PowerShell NativeCommandError로 중단하지 않고 수집
        $ErrorActionPreference = "Continue"
        $output = @(& $Context.HelmPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $outputText = ($output | ForEach-Object {
            $_.ToString()
        }) -join [Environment]::NewLine

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $outputText
    }
}

function Get-AzureSmokeHelmHistory {
    param([object]$Context)

    $result = Invoke-AzureSmokeHelmCommand `
        -Context $Context `
        -Arguments @(
            "history",
            $Context.ReleaseName,
            "--namespace",
            $Context.Namespace,
            "--kube-context",
            $Context.KubernetesContext,
            "--output",
            "json"
        )

    if ($result.ExitCode -ne 0) {
        throw "Helm history command failed. ExitCode=$($result.ExitCode)"
    }

    try {
        $parsedHistory = $result.Output | ConvertFrom-Json
        $history = @(foreach ($entry in $parsedHistory) {
                $entry
            })
    }
    catch {
        throw "Helm history returned invalid JSON. Error=$($_.Exception.Message)"
    }

    if ($history.Count -eq 0) {
        throw "Helm release does not contain any revisions. Release=$($Context.ReleaseName)"
    }

    return @($history | ForEach-Object {
            [pscustomobject]@{
                Revision = [int]$_.revision
                Status = [string]$_.status
                Description = [string]$_.description
            }
        } | Sort-Object Revision)
}

function Get-AzureSmokeApiDeploymentSnapshot {
    param([object]$Context)

    $arguments = @(
        "--context",
        $Context.KubernetesContext,
        "get",
        "deployment/$($Context.ReleaseName)-api",
        "--namespace",
        $Context.Namespace,
        "--output",
        "json"
    )
    $output = @(& $Context.KubectlPath @arguments 2>&1)
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "API Deployment query failed. ExitCode=$exitCode"
    }

    try {
        $deployment = (($output | ForEach-Object {
                    $_.ToString()
                }) -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "API Deployment query returned invalid JSON. Error=$($_.Exception.Message)"
    }

    return [pscustomobject]@{
        Image = [string]$deployment.spec.template.spec.containers[0].image
        DesiredReplicas = [int]$deployment.spec.replicas
        ReadyReplicas = [int]$deployment.status.readyReplicas
        AvailableReplicas = [int]$deployment.status.availableReplicas
    }
}

function Invoke-AzureSmokeRollback {
    param(
        [object]$Context,
        [int]$Revision,
        [int]$TimeoutSeconds
    )

    return Invoke-AzureSmokeHelmCommand `
        -Context $Context `
        -Arguments @(
            "rollback",
            $Context.ReleaseName,
            $Revision.ToString(),
            "--namespace",
            $Context.Namespace,
            "--kube-context",
            $Context.KubernetesContext,
            "--wait",
            "--timeout",
            "${TimeoutSeconds}s"
        )
}

function Invoke-AzureSmokeIntegratedScenario {
    param(
        [string]$ScriptPath,
        [string]$KubernetesContext,
        [string]$Namespace,
        [string]$ReleaseName,
        [int]$ApiLocalPort,
        [int]$GameLocalPort
    )

    & $ScriptPath `
        -KubernetesContext $KubernetesContext `
        -Namespace $Namespace `
        -ReleaseName $ReleaseName `
        -ApiLocalPort $ApiLocalPort `
        -GameLocalPort $GameLocalPort
}

$context = New-AzureSmokeContext `
    -KubernetesContext $KubernetesContext `
    -Namespace $Namespace `
    -ReleaseName $ReleaseName
$baseline = Confirm-AzureSmokeDeployment `
    -Context $context
$baselineHistory = @(Get-AzureSmokeHelmHistory -Context $context)
$latestBaselineRevision = $baselineHistory[-1]

if ($latestBaselineRevision.Revision -ne $baseline.HelmRevision -or
    $latestBaselineRevision.Status -ne "deployed") {
    throw "Latest Helm revision does not match the deployed baseline. DeploymentRevision=$($baseline.HelmRevision), HistoryRevision=$($latestBaselineRevision.Revision), Status=$($latestBaselineRevision.Status)"
}

$target = "release/$ReleaseName revision $($baseline.HelmRevision) in namespace $Namespace"
$action = "Create an expected failed Helm revision and rollback to the deployed baseline"

if (-not $PSCmdlet.ShouldProcess($target, $action)) {
    Write-AzureSmokeStep "Helm failure and rollback test was not approved."
    return
}

Write-AzureSmokeStep "Running baseline HTTP, TCP, and Orleans Smoke Test"
Invoke-AzureSmokeIntegratedScenario `
    -ScriptPath $integratedSmokePath `
    -KubernetesContext $KubernetesContext `
    -Namespace $Namespace `
    -ReleaseName $ReleaseName `
    -ApiLocalPort $ApiLocalPort `
    -GameLocalPort $GameLocalPort

# Baseline Smoke Test 중 동시 배포 발생 여부 재검증
$preMutation = Confirm-AzureSmokeDeployment `
    -Context $context

if ($preMutation.HelmRevision -ne $baseline.HelmRevision -or
    $preMutation.ImageTag -ne $baseline.ImageTag) {
    throw "Helm release changed during the baseline Smoke Test. InitialRevision=$($baseline.HelmRevision), CurrentRevision=$($preMutation.HelmRevision)"
}

$invalidApiTag = "rollback-smoke-missing-$([Guid]::NewGuid().ToString('N'))"
$mutationStarted = $false
$releaseRestored = $false
$testError = $null
$safetyRollbackError = $null
$failedRevision = $null
$rollbackRevision = $null

try {
    Write-AzureSmokeStep `
        "Starting an expected failed Helm upgrade. InvalidApiTag=$invalidApiTag"
    $mutationStarted = $true
    $failedUpgrade = Invoke-AzureSmokeHelmCommand `
        -Context $context `
        -Arguments @(
            "upgrade",
            $ReleaseName,
            $chartPath,
            "--namespace",
            $Namespace,
            "--kube-context",
            $KubernetesContext,
            "--reuse-values",
            "--set-string",
            "images.api.tag=$invalidApiTag",
            "--wait",
            "--wait-for-jobs",
            "--timeout",
            "${FailedUpgradeTimeoutSeconds}s",
            "--description",
            "Intentional API image failure for rollback smoke test"
        )

    if ($failedUpgrade.ExitCode -eq 0) {
        throw "Intentional Helm upgrade unexpectedly succeeded. InvalidApiTag=$invalidApiTag"
    }

    $failedHistory = @(Get-AzureSmokeHelmHistory -Context $context)
    $failedRevision = $failedHistory[-1]

    if ($failedRevision.Revision -le $baseline.HelmRevision -or
        $failedRevision.Status -ne "failed") {
        throw "Helm did not record the expected failed revision. Baseline=$($baseline.HelmRevision), Latest=$($failedRevision.Revision), Status=$($failedRevision.Status)"
    }

    $failedApi = Get-AzureSmokeApiDeploymentSnapshot `
        -Context $context

    if ($failedApi.Image -notlike "*:$invalidApiTag") {
        throw "Failed Helm revision did not apply the expected invalid API image. Image=$($failedApi.Image)"
    }

    if ($failedApi.ReadyReplicas -lt 1 -or
        $failedApi.AvailableReplicas -lt 1) {
        throw "Existing API replica was not kept available during the failed rolling update. Ready=$($failedApi.ReadyReplicas), Available=$($failedApi.AvailableReplicas)"
    }

    Write-AzureSmokeStep `
        "Confirmed failed Helm revision and existing API availability. FailedRevision=$($failedRevision.Revision)"
    Write-AzureSmokeStep `
        "Rolling back to baseline Helm revision $($baseline.HelmRevision)"
    $rollbackResult = Invoke-AzureSmokeRollback `
        -Context $context `
        -Revision $baseline.HelmRevision `
        -TimeoutSeconds $RollbackTimeoutSeconds

    if ($rollbackResult.ExitCode -ne 0) {
        throw "Helm rollback command failed. ExitCode=$($rollbackResult.ExitCode)"
    }

    $restored = Confirm-AzureSmokeDeployment `
        -Context $context

    if ($restored.ImageTag -ne $baseline.ImageTag) {
        throw "Rollback did not restore the baseline image tag. Expected=$($baseline.ImageTag), Actual=$($restored.ImageTag)"
    }

    $rollbackHistory = @(Get-AzureSmokeHelmHistory -Context $context)
    $rollbackRevision = $rollbackHistory[-1]

    if ($rollbackRevision.Revision -le $failedRevision.Revision -or
        $rollbackRevision.Status -ne "deployed" -or
        $rollbackRevision.Revision -ne $restored.HelmRevision) {
        throw "Helm rollback revision is not deployed. FailedRevision=$($failedRevision.Revision), RollbackRevision=$($rollbackRevision.Revision), Status=$($rollbackRevision.Status)"
    }

    $releaseRestored = $true

    Write-AzureSmokeStep "Running post-rollback HTTP, TCP, and Orleans Smoke Test"
    Invoke-AzureSmokeIntegratedScenario `
        -ScriptPath $integratedSmokePath `
        -KubernetesContext $KubernetesContext `
        -Namespace $Namespace `
        -ReleaseName $ReleaseName `
        -ApiLocalPort $ApiLocalPort `
        -GameLocalPort $GameLocalPort

    Write-AzureSmokeStep `
        "Success: Azure Helm rollback Smoke Test completed"
    Write-Host "[azure-smoke] BaselineRevision=$($baseline.HelmRevision), FailedRevision=$($failedRevision.Revision), RollbackRevision=$($rollbackRevision.Revision), RestoredImageTag=$($baseline.ImageTag), FailedApiTag=$invalidApiTag"
}
catch {
    $testError = $_
}
finally {
    if ($mutationStarted -and -not $releaseRestored) {
        Write-AzureSmokeStep `
            "Attempting safety rollback to baseline revision $($baseline.HelmRevision)"

        try {
            $safetyRollback = Invoke-AzureSmokeRollback `
                -Context $context `
                -Revision $baseline.HelmRevision `
                -TimeoutSeconds $RollbackTimeoutSeconds

            if ($safetyRollback.ExitCode -ne 0) {
                throw "Safety rollback command failed. ExitCode=$($safetyRollback.ExitCode)"
            }

            $safetyRestored = Confirm-AzureSmokeDeployment `
                -Context $context

            if ($safetyRestored.ImageTag -ne $baseline.ImageTag) {
                throw "Safety rollback did not restore the baseline image tag. Expected=$($baseline.ImageTag), Actual=$($safetyRestored.ImageTag)"
            }

            $releaseRestored = $true
            Write-AzureSmokeStep "Safety rollback completed"
        }
        catch {
            $safetyRollbackError = $_
        }
    }
}

if ($null -ne $safetyRollbackError) {
    throw "Helm rollback Smoke Test failed and safety rollback also failed. TestError=$($testError.Exception.Message), SafetyRollbackError=$($safetyRollbackError.Exception.Message)"
}

if ($null -ne $testError) {
    throw $testError
}
