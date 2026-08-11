# Blue Server Azure Pulumi

Terraform으로 구성했던 Azure 인프라 일부를 C#과 Pulumi Azure Native Provider로 재현하고 차이를 비교하기 위한 프로젝트입니다.

8-1 단계에서는 Stack Configuration, Naming 규칙, Output과 `pulumi preview` 실행 기반을 구성했습니다. 8-2 단계에서는 Terraform 구성과 비교할 Resource Group과 ACR을 선언합니다.

## 구성

- .NET 10 Console Application
- Pulumi .NET SDK
- Pulumi Azure Native Provider
- `dev` Stack의 Project Name, Pulumi 전용 Resource Name Qualifier, Environment, Azure Region 설정
- Azure Resource Group과 Private Azure Container Registry
- Terraform Output과 비교할 Resource 이름과 ACR Login Server Output

## Pulumi CLI 설치

Windows Package Manager로 Pulumi CLI를 설치합니다.

```powershell
winget install pulumi
```

설치 후 새 PowerShell을 열고 Version을 확인합니다.

```powershell
pulumi version
```

## 로컬 Backend와 Stack 초기화

현재 단계는 별도의 Pulumi Cloud 계정과 Azure State Storage 없이 학습할 수 있도록 Local Backend를 사용합니다. Local State는 사용자 Profile의 `.pulumi` 디렉터리에 저장되며 Git에 포함되지 않습니다.

```powershell
Set-Location infra/pulumi/azure

pulumi login --local
pulumi stack init dev
```

Local Stack의 Configuration 암호화 Passphrase는 Repository에 저장하지 않습니다. 새 PowerShell Process에서 Non-interactive 명령을 실행할 때는 Passphrase를 숨겨서 입력하고 Process 환경 변수로만 전달합니다.

```powershell
$securePulumiPassphrase = Read-Host `
    "PULUMI_CONFIG_PASSPHRASE" `
    -AsSecureString

$pulumiCredential = [PSCredential]::new(
    "pulumi",
    $securePulumiPassphrase)

$env:PULUMI_CONFIG_PASSPHRASE = `
    $pulumiCredential.GetNetworkCredential().Password
```

`Pulumi.dev.yaml`의 `encryptionsalt`는 Passphrase 자체가 아니며 Stack의 Secret 암호화에 사용하는 Salt입니다. Passphrase 없이는 암호화된 값을 복호화할 수 없습니다.

이미 `dev` Stack이 존재한다면 새로 만들지 않고 선택합니다.

```powershell
pulumi stack select dev
```

## Azure 인증과 Preview

Azure Native Provider는 로컬 개발 환경에서 현재 Azure CLI 인증을 사용합니다. 먼저 대상 Subscription을 확인합니다.

```powershell
az account show `
    --query "{Name:name, SubscriptionId:id, TenantId:tenantId}" `
    --output table
```

Subscription ID는 Repository 파일에 기록하지 않고 현재 PowerShell Process에만 전달합니다.

```powershell
$env:ARM_SUBSCRIPTION_ID = az account show `
    --query id `
    --output tsv

pulumi preview --diff
```

Preview에는 Pulumi Stack과 Resource Group·ACR을 포함해 Resource 3개가 생성 대상으로 표시되어야 합니다. `pulumi up`을 실행하기 전에는 실제 Azure Resource와 비용이 발생하지 않습니다.

Terraform과 Pulumi가 동일한 실제 Resource를 서로 다른 State로 관리하지 않도록 Pulumi 전용 `resourceNameQualifier`를 이름에 포함합니다.

```text
Terraform Resource Group: rg-blue-server-dev
Pulumi Resource Group: rg-blue-server-pulumi-dev
Pulumi ACR: acrblueserverpulumidevad7bd252
```

ACR 이름의 마지막 8자는 현재 Azure Native Provider의 Subscription ID를 SHA-1으로 계산한 값입니다. Subscription ID 자체는 Stack Configuration이나 Repository 파일에 저장하지 않습니다.

Preview 검토 전에는 다음 명령을 실행하지 않습니다. KAN-65에서는 분리된 Resource 이름을 확인한 뒤 `pulumi up`과 `pulumi destroy`를 사용자 실행으로 검증합니다.

```powershell
# 실제 Azure Resource를 생성하므로 현재 단계에서는 실행하지 않음
pulumi up
```

검증 후 Process 환경 변수를 제거합니다.

```powershell
Remove-Item Env:ARM_SUBSCRIPTION_ID `
    -ErrorAction SilentlyContinue

Remove-Item Env:PULUMI_CONFIG_PASSPHRASE `
    -ErrorAction SilentlyContinue

$securePulumiPassphrase = $null
$pulumiCredential = $null
```

## .NET 빌드

Pulumi CLI 설치 전에도 C# Program과 Package 참조는 다음 명령으로 검증할 수 있습니다.

```powershell
dotnet build infra/pulumi/azure/blueServer.Infrastructure.Pulumi.Azure.csproj
```

## Resource 의존성

ACR의 `ResourceGroupName`과 `Location`에는 Resource Group의 Output을 전달합니다. Pulumi는 이 데이터 흐름을 바탕으로 Resource Group이 먼저 준비되어야 한다는 의존성을 자동으로 구성합니다.

## 기본 Template를 그대로 사용하지 않은 이유

`pulumi new azure-csharp`의 기본 Template는 Resource Group과 Storage Account 예제를 함께 생성합니다. 이번 단계에서는 기반과 실제 Resource 구현을 커밋 단위로 분리하기 위해 Resource가 없는 최소 프로젝트부터 구성합니다.
