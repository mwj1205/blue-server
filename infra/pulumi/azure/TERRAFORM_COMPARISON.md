# Terraform과 Pulumi 비교

Blue Server에서 실제로 구현한 Azure Resource Group과 ACR을 기준으로 Terraform과 Pulumi의 차이를 비교합니다.

## 비교 전제

- Terraform은 AKS, ACR, PostgreSQL, Redis, GitHub Actions Identity까지 관리하는 주 인프라 구성입니다.
- Pulumi는 학습 범위를 제한하기 위해 Resource Group과 ACR만 재현했습니다.
- Terraform은 Azure Storage Remote Backend, Pulumi는 Local Backend를 사용했습니다.
- 서로 다른 State가 같은 Azure Resource를 소유하지 않도록 실제 Resource 이름을 분리했습니다.

따라서 이번 결과는 두 도구의 전체 기능을 동일 조건에서 비교한 Benchmark가 아니라, Blue Server에 실제 적용한 범위에서 얻은 운영 판단입니다.

## 동일 Resource 선언 비교

| 구분 | Terraform | Pulumi C# |
|---|---|---|
| 입력 설정 | `variable`과 `validation` | `Config.Require`와 C# 조건 검사 |
| 공통 이름 | `locals.name_prefix` | C# 문자열 보간으로 만든 `namePrefix` |
| Resource Group | `azurerm_resource_group` | `AzureNative.Resources.ResourceGroup` |
| ACR | `azurerm_container_registry` | `AzureNative.ContainerRegistry.Registry` |
| Resource 의존성 | Resource 속성 참조 | `Output<T>`를 `Input<T>`에 전달 |
| 결과 노출 | `output` Block | `Deployment.RunAsync` 반환 Dictionary |

Terraform의 ACR은 `azurerm_resource_group.main.name`과 `location`을 참조합니다. Terraform은 이 표현식을 분석해 Resource Group을 먼저 생성합니다.

Pulumi의 ACR은 `resourceGroup.Name`과 `resourceGroup.Location`을 입력으로 받습니다. 두 값은 배포 중 확정되는 `Output<T>`이며, Pulumi는 Output의 데이터 흐름을 이용해 동일한 의존성을 구성합니다. 비동기 결과를 이용해 ACR 이름을 만들 때는 `Apply`가 필요했습니다.

## 코드 구조와 타입 검증

Terraform은 Resource 종류별 `.tf` 파일을 나누더라도 같은 Module 안의 파일을 하나의 선언으로 결합합니다. Resource 설정을 읽기는 간결하고, Provider Schema와 `variable`의 `validation`은 `terraform validate`와 `plan`에서 검증됩니다.

Pulumi는 일반 C# Project이므로 문자열 처리, Hash 계산, 조건 검사에 .NET API를 그대로 사용할 수 있습니다. IDE Refactoring과 Compiler 검사를 받을 수 있지만 Azure Resource 속성의 유효성은 C# Compile만으로 모두 확인되지 않으며, Provider 실행과 `pulumi preview`가 여전히 필요합니다.

현재 구현에서는 ACR SKU 검증을 다음과 같이 나눴습니다.

- Terraform: `container_registry_sku` Variable의 `validation`
- Pulumi: `supportedContainerRegistrySkus` 검사 후 `RunException`

Pulumi에서 C#을 사용해도 Infrastructure 검증이 자동으로 해결되는 것은 아니며, Application 수준 검증과 Provider 검증의 경계를 직접 관리해야 합니다.

## State와 Configuration

| 구분 | Terraform | Pulumi |
|---|---|---|
| State 단위 | Terraform State | Pulumi Stack |
| 현재 Backend | Azure Storage의 `azurerm` Backend | 사용자 PC의 Local Backend |
| 환경 설정 | Variable, Process 환경 변수 | `Pulumi.<stack>.yaml`, Process 환경 변수 |
| 변경 검토 | 저장된 Plan 또는 화면 출력 | Preview 화면 출력 |
| Resource 제거 후 | State는 유지 | Stack은 유지되며 별도 `stack rm` 필요 |

Terraform Remote State는 GitHub Actions나 다른 PC가 같은 State를 공유할 기반이 있습니다. 반면 현재 Pulumi Local State는 사용자 PC에만 있으므로 팀 작업이나 CI에서 그대로 사용할 수 없습니다. Pulumi를 운영에 사용하려면 Pulumi Cloud 또는 Azure Blob 같은 공유 Backend와 동시 Update 제어를 먼저 결정해야 합니다.

`Pulumi.dev.yaml`의 `encryptionsalt`는 Password가 아닙니다. Local Stack의 Secret을 복호화하는 Passphrase는 Repository가 아닌 Process 환경 변수로 전달했습니다. 다만 이번 Pulumi 범위에는 실제 Secret Resource나 `pulumi config set --secret`을 적용하지 않았으므로, Pulumi Secret 운영은 검증 완료 범위에 포함하지 않습니다.

Terraform의 PostgreSQL Password는 `sensitive`와 `ephemeral`을 적용해 CLI 노출과 State 저장을 제한했습니다. 이 기능은 Terraform 전체 인프라에서 실제 적용한 차이입니다.

## 실행 흐름 비교

| 목적 | Terraform | Pulumi |
|---|---|---|
| Backend 연결 | `terraform init` | `pulumi login --local` |
| 환경 선택 | Backend Key와 Variable | `pulumi stack select dev` |
| 정적 검증 | `terraform validate` | `dotnet build` |
| 변경 검토 | `terraform plan` | `pulumi preview --diff` |
| Resource 생성 | `terraform apply` | `pulumi up --diff` |
| Resource 제거 | `terraform destroy` | `pulumi destroy --diff` |
| 환경 State 제거 | Backend를 별도로 정리 | `pulumi stack rm` |

두 도구 모두 변경 계획과 실제 적용을 분리할 수 있습니다. 이번 Pulumi 검증에서는 `preview`의 `3 to create`를 확인한 뒤 `up`을 실행했고, Azure Portal에서 Resource Group과 ACR 생성을 확인했습니다. 이후 `destroy`, Resource 수 0 확인, `stack rm`과 Azure 잔여 Resource 확인까지 수행했습니다.

## 재사용과 CI 적용 범위

Terraform Module과 Pulumi `ComponentResource`는 모두 현재 코드에 도입하지 않았습니다. 따라서 재사용성은 이번 구현만으로 우열을 결론내리지 않습니다.

Terraform 구성은 Remote State와 GitHub Actions용 Azure OIDC Identity까지 마련되어 있습니다. 다만 Infrastructure `plan`과 `apply` 자체를 실행하는 GitHub Actions Workflow는 아직 없으므로 Terraform 자동 배포도 완료 상태는 아닙니다.

Pulumi는 Local Backend만 사용했고 GitHub Actions Workflow가 없습니다. CI에 연결하려면 다음 항목이 먼저 필요합니다.

1. 공유 Backend 선택
2. OIDC를 사용하는 Azure 인증 연결
3. Pull Request의 `pulumi preview`와 승인된 Branch의 `pulumi up` 분리
4. Stack별 동시 Update 방지와 복구 절차 구성

## 현재 프로젝트에서 확인한 장단점

### Terraform

장점:

- Azure 전체 인프라와 Remote State가 이미 구성되어 운영 흐름이 가장 많이 검증됨
- HCL에서 Resource와 의존 관계를 짧고 명시적으로 확인 가능
- 저장한 Plan을 검토한 뒤 같은 Plan을 Apply하는 흐름 사용 가능

주의점:

- C# Project와 별도의 HCL 문법과 Provider 동작을 학습해야 함
- Azure가 기본값을 보완한 속성 때문에 Plan Drift가 발생할 수 있음
- Write-only Password처럼 State에 저장하지 않는 값은 별도 Version과 주입 흐름이 필요함

### Pulumi C#

장점:

- 기존 C# 지식과 .NET API를 Naming, 검증, 계산에 재사용 가능
- Compiler와 IDE의 탐색·Refactoring 지원을 받을 수 있음
- Resource Output을 다른 Resource Input으로 연결하는 의존성이 코드의 데이터 흐름으로 표현됨

주의점:

- `Input<T>`, `Output<T>`, `Apply`의 실행 시점을 별도로 이해해야 함
- 일반 C# Package 의존성도 관리 대상이며, 이번 구성에서는 OpenTelemetry 전이 의존성 Version을 직접 보완해야 했음
- Local Backend는 개인 학습에는 간단하지만 팀·CI 운영 기반으로는 부족함

## Blue Server 적용 결론

Blue Server의 주 IaC 도구는 Terraform을 유지합니다.

현재 Terraform은 AKS, ACR, PostgreSQL, Redis, OIDC Identity와 Remote State까지 관리합니다. 이를 Pulumi로 다시 작성하면 기능 학습보다 Migration과 이중 State 위험에 더 많은 시간이 들고, 이미 검증한 배포 기반을 다시 검증해야 합니다.

Pulumi는 다음 조건에서 제한적으로 사용합니다.

- 기존 Terraform Resource와 소유권이 겹치지 않는 독립적인 실험
- C# 기반 `ComponentResource`나 Automation API가 실제로 필요한 기능
- 공유 Backend와 CI 운영 기준을 먼저 마련할 수 있는 신규 Resource 영역

같은 실제 Azure Resource를 Terraform State와 Pulumi Stack에 동시에 등록하지 않습니다. 도구를 변경해야 한다면 기존 State에서 제거하고 새 State로 Import하는 명시적인 Migration 작업으로 진행합니다.

## 검증 결과

- Pulumi Preview: 성공, Stack과 Azure Resource를 포함해 `3 to create`
- Pulumi Up: 성공, 전용 Resource Group과 ACR 생성 확인
- Pulumi Destroy: 성공, Stack Resource 0 확인
- Pulumi Stack 제거: 성공, `Pulumi.dev.yaml` 보존
- Azure 잔여 Resource: Pulumi·Terraform Resource Group 모두 없음

