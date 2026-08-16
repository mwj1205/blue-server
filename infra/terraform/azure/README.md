# Blue Server Azure Terraform

Blue Server의 Azure 인프라를 Terraform으로 관리하는 Root Module입니다.

현재 단계에서는 Application Resource Group, Azure Container Registry, 학습용 Azure Kubernetes Service와 Azure Database for PostgreSQL Flexible Server를 정의하고 Azure Blob Backend에 State를 저장합니다.

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

## GitHub Actions Azure OIDC 인증

GitHub Actions는 Client Secret 대신 User Assigned Managed Identity와 Federated Credential을 사용하여 Azure에 로그인합니다.

Terraform 적용 후 다음 Output을 GitHub Repository의 `Settings > Secrets and variables > Actions`에 등록합니다.

| 구분     | 이름                    | 값                                               |
| -------- | ----------------------- | ------------------------------------------------ |
| Secret   | `AZURE_CLIENT_ID`       | `terraform output -raw github_actions_client_id` |
| Secret   | `AZURE_TENANT_ID`       | `terraform output -raw github_actions_tenant_id` |
| Secret   | `AZURE_SUBSCRIPTION_ID` | `az account show --query id --output tsv`        |
| Variable | `AZURE_RESOURCE_GROUP`  | `terraform output -raw resource_group_name`      |

등록되는 세 ID는 인증서나 Password가 아니지만 Workflow 설정값의 노출 범위를 줄이기 위해 Secret으로 관리합니다.

Federated Credential은 `mwj1205/blue-server` Repository의 `main` Branch에서 발급된 OIDC Token만 허용합니다. Pull Request나 다른 Branch의 Workflow는 이 Identity로 Azure에 로그인할 수 없습니다.

`.github/workflows/azure-oidc.yml`은 `main` 반영 후 자동 실행되며, 수동 실행도 지원합니다. Azure Login 이후 `rg-blue-server-dev` 조회가 성공하면 OIDC 인증과 Reader 권한이 모두 검증된 것입니다.

## GitHub Actions ACR Push

GitHub Actions용 Managed Identity에는 Resource Group 조회를 위한 `Reader`와 ACR Image Push를 위한 Registry 범위 `AcrPush` 역할을 각각 부여합니다. `AcrPush`는 다른 Azure Resource를 생성하거나 변경할 수 있는 권한을 포함하지 않습니다.

Docker Workflow는 Pull Request와 `main` Push를 분리합니다.

- Pull Request: API, Game, Migrations, Silo Image Build와 Helm Chart 정적 검증
- `main` Push: GitHub OIDC 인증 후 네 Image를 ACR에 Push
- Image Tag: Workflow를 실행한 전체 Git Commit SHA

ACR 이름과 Login Server는 Workflow에 하드코딩하지 않습니다. `AZURE_RESOURCE_GROUP` Repository Variable을 기준으로 대상 Resource Group의 단일 ACR을 조회하며, ACR이 없거나 두 개 이상이면 Push를 중단합니다.

## 학습용 AKS 구성

AKS는 `Free` Tier Control Plane과 `Standard_D4s_v5` System Node 2개로 구성합니다. `Free` Tier는 Control Plane의 Uptime SLA를 제공하지 않으며 Node VM, Managed Disk, Load Balancer, Public IP 등 Cluster가 사용하는 Azure Resource에는 비용이 발생할 수 있습니다.

Kubernetes Version은 ECK `3.4.1`의 공식 지원 범위에 맞춰 Korea Central에서 지원되는 `1.35`로 고정합니다. 특정 Patch를 고정하지 않아 Azure가 지원하는 최신 `1.35.x` Patch를 선택할 수 있도록 합니다. System Node OS는 `AzureLinux3`, Network는 Azure CNI Overlay를 사용합니다. Cluster는 System Assigned Managed Identity를 사용하므로 Service Principal의 Client Secret을 저장하지 않습니다.

로컬 Kubernetes `1.36`과 Minor Version을 맞추는 것보다 Azure 관측 환경에서 사용하는 ECK 안정판의 공식 호환 범위를 우선합니다. ECK가 Kubernetes `1.36`을 공식 지원하는 안정판으로 갱신되면 Operator와 AKS Version을 함께 검토합니다.

Node Pool의 `upgrade_settings`는 Azure가 생성 시 적용한 기본값과 Terraform 구성을 일치시킵니다. 이를 생략하면 IAM처럼 Cluster 외부 Resource만 추가하는 Plan에서도 Provider가 기존 Upgrade 설정 제거를 AKS Update로 제안할 수 있습니다.

System Node Pool의 최소 Node 수와 VM 요구사항을 만족하려면 DSv5 Family vCPU Quota가 최소 8이어야 합니다. 현재 Subscription의 Korea Central Quota 8을 모두 사용하므로 Node 추가나 Kubernetes Upgrade의 Surge Node가 필요하면 먼저 Quota를 늘려야 합니다. 운영 환경에서는 System Node를 3개 이상으로 늘리고 Application을 별도 User Node Pool로 분리해야 합니다.

다음 명령으로 AKS 생성 계획만 검토합니다. 사용자 승인 전에는 `terraform apply`를 실행하지 않습니다.

```powershell
terraform plan -out blue-server.tfplan
terraform show blue-server.tfplan
```

적용 후에는 Terraform Output으로 Azure CLI 접속 명령을 확인할 수 있습니다. Kubeconfig 원문은 별도 Terraform Output이나 Git 추적 파일로 노출하지 않습니다. AzureRM Provider가 State에 저장하는 민감한 Cluster 속성은 기존 Azure Blob 원격 State의 Microsoft Entra ID 인증으로 보호합니다.

```powershell
terraform output -raw aks_get_credentials_command
```

## AKS와 ACR 연결

Private ACR의 Admin 계정을 활성화하지 않고 AKS의 Kubelet Managed Identity에 Registry 범위의 `AcrPull` 역할을 부여합니다. Control Plane Identity는 Cluster Resource 관리에 사용되고, 실제 Container Image Pull은 각 Node의 Kubelet이 수행하므로 `kubelet_identity.object_id`를 Role Assignment의 Principal로 사용합니다.

Cluster가 중지된 상태에서도 Role Assignment를 생성하고 IAM 설정을 확인할 수 있습니다.

```powershell
$kubeletObjectId = terraform output -raw aks_kubelet_identity_object_id
$acrId = terraform output -raw container_registry_id

az role assignment list `
    --assignee $kubeletObjectId `
    --scope $acrId `
    --query "[?roleDefinitionName=='AcrPull'].{Role:roleDefinitionName, PrincipalId:principalId, Scope:scope}" `
    --output table
```

실제 Image Pull 경로 검증은 Cluster를 시작한 뒤 실행합니다.

```powershell
az aks check-acr `
    --resource-group rg-blue-server-dev `
    --name aks-blue-server-dev `
    --acr $(terraform output -raw container_registry_login_server)
```

학습 종료 후 AKS만 먼저 제거할 때는 Target Plan을 저장하고 내용을 확인한 뒤 적용합니다. Target 제거 후 일반 `terraform plan`은 코드에 남아 있는 AKS를 다시 생성 대상으로 표시합니다.

```powershell
terraform plan -destroy `
    -target=azurerm_kubernetes_cluster.main `
    -out aks-destroy.tfplan

terraform show aks-destroy.tfplan
terraform apply aks-destroy.tfplan
```

## Azure Database for PostgreSQL

Application Database를 AKS Pod 수명에서 분리하기 위해 Azure Database for PostgreSQL Flexible Server를 사용합니다. 학습용 개발 환경은 Korea Central에서 지원되는 PostgreSQL 18, Burstable `B_Standard_B1ms`, 32GiB Storage, 7일 Backup으로 구성합니다. 고가용성과 Geo-redundant Backup은 활성화하지 않으며 Storage Auto-grow도 비용 상한을 예측할 수 있도록 비활성화합니다.

현재 AKS는 Managed Load Balancer의 Static Outbound Public IP 하나를 사용합니다. PostgreSQL은 Public Endpoint를 사용하지만 Firewall에는 해당 AKS Outbound IP만 등록합니다. `0.0.0.0`으로 모든 Azure Service를 허용하거나 로컬 Public IP를 기본 허용하지 않습니다.

관리자 Password는 Terraform의 Ephemeral Variable과 AzureRM Provider의 Write-only Argument를 사용합니다. Git, Plan, Terraform State에는 저장하지 않으며 현재 PowerShell Process의 환경 변수로만 전달합니다.

```powershell
$securePassword = Read-Host `
    "PostgreSQL administrator password" `
    -AsSecureString

$env:TF_VAR_postgresql_administrator_password = `
    [System.Net.NetworkCredential]::new("", $securePassword).Password

terraform fmt -check
terraform validate
terraform plan -out postgresql.tfplan
terraform show postgresql.tfplan

Remove-Item Env:TF_VAR_postgresql_administrator_password
```

Password를 회전할 때는 새 값을 외부에서 주입하고 `postgresql_administrator_password_version`을 증가시킵니다. Version이 바뀌어야 Terraform이 Write-only Password 변경을 Azure에 다시 전달합니다.

PostgreSQL 생성 후 다음 Output을 Helm의 Azure Values와 Kubernetes Secret 구성에 사용합니다. Password는 Output으로 제공하지 않습니다.

```powershell
terraform output -raw postgresql_server_fqdn
terraform output -raw postgresql_database_name
terraform output -raw postgresql_administrator_login
terraform output -raw postgresql_aks_firewall_ip
```

개발 환경은 학습 후 Resource를 제거해야 하므로 Terraform `prevent_destroy`를 설정하지 않습니다. 실제 운영 Database에는 별도 환경에서 삭제 보호와 장기 Backup 정책을 적용해야 합니다.

## Azure Managed Redis

Application Cache와 Orleans membership을 Pod 수명에서 분리하기 위해 Azure Managed Redis를 사용합니다. 학습용 개발 환경은 가장 작은 `Balanced_B0` SKU와 High Availability 비활성화를 사용합니다.

현재 AKS Network를 Terraform이 직접 소유하지 않으므로 첫 연결 검증은 Public Endpoint로 진행합니다. Redis Database는 TLS 전용 `Encrypted` Protocol과 Access Key 인증을 사용합니다. Public Endpoint는 모든 Network에서 접근 가능한 개발용 임시 구성이고, 실제 운영 환경에서는 별도 VNet과 Private Endpoint를 구성한 뒤 Public Network Access를 비활성화해야 합니다.

기존 단일 Redis 동작과 Orleans membership 호환성을 우선하여 `NoCluster` Policy를 사용합니다. Eviction은 membership Key가 메모리 압박으로 조용히 제거되지 않도록 `NoEviction`으로 설정합니다.

```powershell
terraform plan -out managed-redis.tfplan
terraform show managed-redis.tfplan
```

생성 후 Host와 TLS Port는 다음 Output으로 확인합니다. Access Key는 민감정보이므로 Terraform Output으로 노출하지 않습니다.

```powershell
terraform output -raw managed_redis_hostname
terraform output -raw managed_redis_port
```

## 예상 Plan 결과

현재 구성의 Plan에는 다음 Resource가 포함되어야 합니다.

```text
azurerm_resource_group.main
azurerm_container_registry.main
azurerm_user_assigned_identity.github_actions
azurerm_federated_identity_credential.github_actions_main
azurerm_role_assignment.github_actions_resource_group_reader
azurerm_role_assignment.github_actions_acr_push
azurerm_kubernetes_cluster.main
azurerm_role_assignment.aks_acr_pull
azurerm_postgresql_flexible_server.main
azurerm_postgresql_flexible_server_database.main
azurerm_postgresql_flexible_server_firewall_rule.aks
azurerm_managed_redis.main
```

Managed Redis 생성 후에는 Azure Host·TLS Port·Access Key를 Helm과 Kubernetes Secret에 연결합니다.
