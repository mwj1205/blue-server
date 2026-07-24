# Blue Server Azure Terraform

Blue Server의 Azure 인프라를 Terraform으로 관리하는 Root Module입니다.

현재 단계에서는 `Resource Group`만 정의하며, `terraform apply`는 실행하지 않습니다.

## 필요 도구

- Terraform `1.15.x`
- Azure CLI
- Azure Subscription

## 로컬 인증과 Subscription 설정

```powershell
az login

$env:TF_VAR_subscription_id = az account show `
    --query id `
    --output tsv
```

AzureRM Provider 4.x는 `plan`과 `apply` 시 대상 Subscription ID를 요구합니다.
`TF_VAR_subscription_id`를 사용하면 실제 Subscription ID를 저장소 파일에 기록하지 않아도 됩니다.

## 초기화와 검증

```powershell
Set-Location infra/terraform/azure

terraform init
terraform fmt -check
terraform validate
terraform plan -out blue-server.tfplan
```

`terraform init`이 생성하는 `.terraform.lock.hcl`은 Provider 버전을 재현하기 위해 Git에 포함합니다.

저장된 Plan 파일과 State, `terraform.tfvars`는 디렉터리의 `.gitignore`에서 제외합니다.

## 예상 Plan 결과

현재 구성의 Plan에는 다음 Resource 하나만 포함되어야 합니다.

```text
azurerm_resource_group.main
```

ACR, AKS, PostgreSQL, Redis는 이후 Jira 작업에서 각각 추가합니다.
