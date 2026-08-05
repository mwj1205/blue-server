# Azure Terraform State Bootstrap

Blue Server Terraform State를 저장할 Azure Storage를 최초 한 번 생성하는 Root Module입니다.

State Storage가 생성되기 전에는 자기 자신을 원격 Backend로 사용할 수 없으므로 이 Module만 로컬 State를 사용합니다.

## 생성 Resource

- `rg-blue-server-tfstate` Resource Group
- Subscription ID에서 결정적으로 계산한 이름의 Storage Account
- `tfstate` Private Blob Container
- 현재 Azure CLI Principal의 `Storage Blob Data Contributor` Role Assignment

Storage Account 이름은 다음 규칙으로 구성합니다.

```text
st + project_name 앞 10자 + tf + subscription_id SHA-1 앞 8자
```

실제 Subscription ID는 이름에 포함되지 않습니다.

## 초기화와 Plan

프로젝트 루트에서 다음 명령을 실행합니다.

```powershell
$env:TF_VAR_subscription_id = az account show `
    --query id `
    --output tsv

Set-Location infra/terraform/bootstrap/azure-state

terraform init
terraform fmt -check
terraform validate
terraform plan -out azure-state.tfplan
```

Plan에는 Resource Group, Storage Account, Private Container, Role Assignment 생성만 포함되어야 합니다.

AzureRM Provider가 Plan 과정에서 암묵적으로 Resource Provider를 등록하지 않도록 자동 등록을 비활성화했습니다. Apply 전에 필요한 Provider 상태를 확인합니다.

```powershell
az provider show `
    --namespace Microsoft.Storage `
    --query registrationState `
    --output tsv

az provider show `
    --namespace Microsoft.Authorization `
    --query registrationState `
    --output tsv
```

`Registered`가 아니라면 실제 Apply 전에 명시적으로 등록해야 합니다.

## Apply

Plan과 비용을 확인한 후에만 실행합니다.

```powershell
terraform apply azure-state.tfplan
```

Apply 후 Backend 연결에 필요한 값을 확인합니다.

```powershell
terraform output
```

Bootstrap의 로컬 `terraform.tfstate`는 Git에서 제외됩니다. State Storage를 삭제하거나 Bootstrap을 다른 PC에서 변경하려면 기존 State 보존 또는 Resource Import가 필요합니다.

## 보안 선택

- Backend 인증은 Storage Access Key가 아닌 Microsoft Entra ID 사용
- Blob과 Container 외부 공개 차단
- HTTPS와 TLS 1.2 강제
- Blob Versioning과 7일 삭제 보존
- 로컬 PC와 GitHub Hosted Runner 접근을 위해 Public Network Endpoint 유지

`shared_access_key_enabled`는 AzureRM Provider가 Container를 안정적으로 생성할 수 있도록 유지하지만 Backend 인증에는 사용하지 않습니다. Private Endpoint는 AKS Network 구성이 준비된 뒤 별도 판단합니다.
