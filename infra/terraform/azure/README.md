# Blue Server Azure Terraform

Blue Server의 Azure 인프라를 Terraform으로 관리하는 Root Module입니다.

현재 단계에서는 Application Resource Group과 Azure Container Registry를 정의하고 Azure Blob Backend에 State를 저장합니다.

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

## Backend 초기화와 검증

먼저 `../bootstrap/azure-state`에서 State Storage를 생성해야 합니다.

```powershell
Push-Location infra/terraform/bootstrap/azure-state

$stateStorageAccountName = terraform output `
    -raw storage_account_name

$stateContainerName = terraform output `
    -raw container_name

Pop-Location

Set-Location infra/terraform/azure

terraform init -reconfigure `
    -backend-config="storage_account_name=$stateStorageAccountName" `
    -backend-config="container_name=$stateContainerName" `
    -backend-config="key=blue-server/dev.terraform.tfstate" `
    -backend-config="use_azuread_auth=true" `
    -backend-config="use_cli=true"

terraform fmt -check
terraform validate
terraform plan -out blue-server.tfplan
```

`terraform init`이 생성하는 `.terraform.lock.hcl`은 Provider 버전을 재현하기 위해 Git에 포함합니다.

저장된 Plan 파일과 State, `terraform.tfvars`는 디렉터리의 `.gitignore`에서 제외합니다.

Backend 설정에는 Access Key나 SAS Token을 사용하지 않습니다. 로컬에서는 Azure CLI의 Microsoft Entra ID 인증을 사용하고, GitHub Actions에서는 후속 OIDC 작업에서 같은 Backend에 접근합니다.

## ACR 이미지 Push와 Pull 수동 검증

GitHub Actions로 자동화하기 전에 Azure CLI 인증과 Docker Registry 경로가 정상적으로 동작하는지 API 이미지 하나로 검증합니다.

저장소 루트에서 다음 명령을 실행합니다.

```powershell
Push-Location infra/terraform/azure

$registryName = terraform output `
    -raw container_registry_name

$loginServer = terraform output `
    -raw container_registry_login_server

Pop-Location

$imageTag = git rev-parse --short HEAD
$image = "$loginServer/blue-server-api:manual-$imageTag"

az acr login `
    --name $registryName

docker build `
    --file src/blueServer.Api/Dockerfile `
    --tag $image `
    .

docker push $image

# 로컬 이미지 제거 후 ACR에서 다시 내려받아 Registry 왕복 검증
docker image rm $image
docker pull $image

az acr repository show-tags `
    --name $registryName `
    --repository blue-server-api `
    --detail `
    --output table
```

`az acr login`은 ACR Admin 계정을 활성화하지 않고 현재 Azure CLI의 Microsoft Entra ID 인증을 Docker에 전달합니다.

Push와 Pull 결과의 Digest가 같고 `az acr repository show-tags`에서 `manual-<Git SHA>` 태그가 조회되면 검증에 성공한 것입니다.

## 예상 Plan 결과

현재 구성의 Plan에는 다음 Resource 두 개가 포함되어야 합니다.

```text
azurerm_resource_group.main
azurerm_container_registry.main
```

AKS, PostgreSQL, Redis는 이후 Jira 작업에서 각각 추가합니다.
