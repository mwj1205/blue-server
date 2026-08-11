# Blue Server Azure Pulumi

Terraform으로 구성했던 Azure 인프라 일부를 C#과 Pulumi Azure Native Provider로 재현하고 차이를 비교하기 위한 프로젝트입니다.

현재 8-1 단계에서는 Azure Resource를 선언하지 않습니다. Stack Configuration, Naming 규칙, Output과 `pulumi preview` 실행 기반만 구성하며 Resource Group과 ACR은 8-2 단계에서 추가합니다.

## 구성

- .NET 10 Console Application
- Pulumi .NET SDK
- Pulumi Azure Native Provider
- `dev` Stack의 Project Name, Environment, Azure Region 설정
- Azure Resource를 생성하지 않는 Configuration 검증용 Output

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

pulumi preview
```

현재 단계의 Preview에는 Azure Resource 생성이 없어야 합니다. `projectName`, `environment`, `location`, `namePrefix` Output만 확인합니다.

검증 후 Process 환경 변수를 제거합니다.

```powershell
Remove-Item Env:ARM_SUBSCRIPTION_ID `
    -ErrorAction SilentlyContinue
```

## .NET 빌드

Pulumi CLI 설치 전에도 C# Program과 Package 참조는 다음 명령으로 검증할 수 있습니다.

```powershell
dotnet build infra/pulumi/azure/blueServer.Infrastructure.Pulumi.Azure.csproj
```

## 기본 Template를 그대로 사용하지 않은 이유

`pulumi new azure-csharp`의 기본 Template는 Resource Group과 Storage Account 예제를 함께 생성합니다. 이번 단계에서는 기반과 실제 Resource 구현을 커밋 단위로 분리하기 위해 Resource가 없는 최소 프로젝트부터 구성합니다.
