# 로컬 검증 스크립트

## Azure AKS HTTP·TCP·Orleans smoke test

Azure smoke test는 AKS의 Pod가 Ready인지 확인하는 데서 끝나지 않고 실제 게임 요청 흐름을 검증합니다. API와 Game Service는 `ClusterIP`를 유지하며, Script가 실행되는 동안에만 `kubectl port-forward`로 로컬 Port를 연결합니다.

### Script 선택

- `azure-http-smoke.ps1`: Register·Login·JWT PlayerProfile HTTP 경로만 빠르게 검증
- `azure-tcp-smoke.ps1`: HTTP와 TCP Profile 비교, Orleans Grain Activation, Redis Clustering, 민감정보 Log 미노출까지 통합 검증
- `smoke/BlueServer.AzureSmoke.psm1`: 두 Entry Script가 공유하는 AKS·인증·Packet·Log 검증 기능

배포 전체 흐름을 확인할 때는 `azure-tcp-smoke.ps1`을 사용합니다. 이 Script가 HTTP 검증도 포함하므로 두 Entry Script를 연속으로 실행할 필요는 없습니다.

### 사전 조건

- Azure AKS Cluster가 실행 중인 상태
- `kubectl`, `helm` 명령 사용 가능
- `az aks get-credentials` 등을 통해 대상 AKS Context가 로컬 kubeconfig에 등록된 상태
- 대상 Namespace에 Helm Release가 `deployed` 상태
- API 1개, Game 1개, Silo 2개 Replica가 Ready 상태
- API·Game·Silo가 동일한 40자리 Git SHA Image Tag를 사용하는 상태
- 기본 로컬 Port `5201`, `7777`을 다른 Process가 사용하지 않는 상태

현재 기본 대상은 다음과 같습니다.

| 항목 | 기본값 |
|---|---|
| Kubernetes Context | `aks-blue-server-dev` |
| Namespace | `blue-dev` |
| Helm Release | `blue-server` |
| API Local Port | `5201` |
| Game Local Port | `7777` |

### HTTP 빠른 검증

저장소 루트에서 다음 명령을 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\azure-http-smoke.ps1 `
  -KubernetesContext aks-blue-server-dev `
  -Namespace blue-dev `
  -ReleaseName blue-server
```

다음 흐름을 검증합니다.

1. AKS Context·Namespace·Helm Release 확인
2. API·Game·Silo Deployment와 `ClusterIP` Service 확인
3. API Service Port Forward
4. 임시 Player Register·Login
5. JWT를 사용한 HTTP PlayerProfile 조회

마지막에 다음 메시지가 출력되면 성공입니다.

```text
[azure-smoke] Success: Azure HTTP Smoke Test completed
```

### HTTP·TCP·Orleans 통합 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\azure-tcp-smoke.ps1 `
  -KubernetesContext aks-blue-server-dev `
  -Namespace blue-dev `
  -ReleaseName blue-server
```

HTTP 검증에 더해 다음 흐름을 검증합니다.

1. Silo 2개의 Kubernetes Hosting·Redis Clustering 설정 Log 확인
2. Game Service Port Forward
3. JWT를 사용한 TCP Login·PlayerProfile Packet 요청
4. HTTP와 TCP의 Player ID·Nickname·재화·집계 필드 비교
5. 신규 PlayerProfile Grain Activation이 전체 Silo에서 한 건인지 확인
6. API·Game·Silo Log에 Password·Access Token·Refresh Token 원문이 없는지 확인

마지막에 다음 메시지와 실행 결과 요약이 출력되면 성공입니다.

```text
[azure-smoke] Success: Azure HTTP, TCP, and Orleans Smoke Test completed
[azure-smoke] HelmRevision=..., ImageTag=..., PlayerId=..., GrainPod=..., GrainActivations=1
```

### Port와 Timeout 변경

기본 로컬 Port를 사용 중이거나 Grain Activation Log 반영을 더 기다려야 한다면 Parameter를 변경합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\azure-tcp-smoke.ps1 `
  -KubernetesContext aks-blue-server-dev `
  -Namespace blue-dev `
  -ReleaseName blue-server `
  -ApiLocalPort 15201 `
  -GameLocalPort 17777 `
  -PortForwardStartupTimeoutSeconds 60 `
  -GrainActivationTimeoutSeconds 30
```

### 실행 시 주의사항

- Script는 매번 `smoke-<timestamp>-<random>` 형식의 임시 Player를 생성합니다.
- 안전한 Player 삭제 API가 없으므로 생성된 Player Row는 Managed PostgreSQL에 남습니다.
- Password와 JWT는 Process Memory에서만 사용하며 성공 결과에 출력하지 않습니다.
- 성공과 실패 모두에서 `finally`를 통해 인증 값 참조와 Port Forward Process를 정리합니다.
- 검증 실패 시 Script는 실패 단계와 원인을 출력하고 0이 아닌 Exit Code로 종료합니다.

### Silo Pod 장애 복구 검증

`azure-silo-recovery-smoke.ps1`은 신규 PlayerProfile Grain을 활성화한 Silo Pod를 정확히 식별해 삭제하고, Kubernetes의 Pod 교체와 Orleans Grain 재활성화를 검증합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\azure-silo-recovery-smoke.ps1 `
  -KubernetesContext aks-blue-server-dev `
  -Namespace blue-dev `
  -ReleaseName blue-server
```

실제 Pod 삭제 직전에 PowerShell 확인 Prompt가 표시됩니다. 대상 Context·Namespace·Pod 이름을 확인한 뒤에만 실행을 승인합니다.

다음 흐름을 검증합니다.

1. 신규 Player의 HTTP·TCP PlayerProfile과 Grain Activation Pod 확인
2. 장애 전 API·Game·Silo Log의 민감정보 미노출 확인
3. 활성 Grain을 소유한 Silo Pod 삭제
4. 기존 2개 Silo Replica와 새 Pod의 Ready 상태 복구 확인
5. 같은 Player와 JWT로 HTTP·TCP Profile 재조회
6. 장애 전후 Profile 전체 필드 일치 확인
7. 삭제되지 않은 Silo에서 Grain 재활성화 확인

마지막에 다음 메시지와 삭제·교체·재활성화 Pod 요약이 출력되면 성공입니다.

```text
[azure-smoke] Success: Azure Silo recovery Smoke Test completed
[azure-smoke] HelmRevision=..., ImageTag=..., PlayerId=..., DeletedSiloPod=..., ReplacementSiloPod=..., ReactivatedGrainPod=..., RecoveryAttempts=...
```

`-WhatIf`를 사용하면 Silo Pod는 삭제하지 않습니다. 단, 삭제 대상 Grain을 식별하는 사전 검증 과정에서 임시 Player는 생성됩니다. 다른 Deployment나 Rollout이 진행 중일 때는 Pod 교체 결과를 정확히 판단할 수 없으므로 실행하지 않습니다.

### API Pod 장애 복구 검증

`azure-api-recovery-smoke.ps1`은 단일 API Pod를 삭제하고, Kubernetes의 Pod 교체와 동일 JWT를 사용한 HTTP PlayerProfile 복구를 검증합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\azure-api-recovery-smoke.ps1 `
  -KubernetesContext aks-blue-server-dev `
  -Namespace blue-dev `
  -ReleaseName blue-server
```

다음 흐름을 검증합니다.

1. 신규 Player 생성과 장애 전 HTTP PlayerProfile 조회
2. API Log의 Password·Access Token·Refresh Token 원문 미노출 확인
3. API Pod 삭제와 동일 Replica 수의 교체 Pod Ready 확인
4. 교체 Pod를 대상으로 API Service Port Forward 재연결
5. 동일 JWT를 사용한 HTTP PlayerProfile 재조회
6. 장애 전후 Player ID·Nickname·재화·집계 필드 비교
7. 교체 API Pod Log의 인증 값 원문 미노출 확인

`kubectl port-forward service/...`는 연결을 시작할 때 선택된 Pod에 고정됩니다. 기존 API Pod가 삭제되면 연결도 종료되므로, Script는 교체 Pod가 Ready가 된 후 Port Forward를 새로 시작합니다.

마지막에 다음 메시지와 API Pod 교체 요약이 출력되면 성공입니다.

```text
[azure-smoke] Success: Azure API recovery Smoke Test completed
[azure-smoke] HelmRevision=..., ImageTag=..., PlayerId=..., DeletedApiPod=..., ReplacementApiPod=..., RecoveryAttempts=...
```

실제 Pod 삭제 직전에 PowerShell 확인 Prompt가 표시됩니다. 현재 검증은 요청을 처리한 Pod를 명확히 특정하기 위해 API Replica가 하나일 때만 실행됩니다. `-WhatIf`를 사용하면 임시 Player 생성과 사전 검증까지만 수행하고 API Pod 삭제는 생략합니다.

## ELK·Elastic APM 통합 smoke test

`observability-smoke.ps1`은 컨테이너가 실행 중인지만 검사하지 않고 다음 전체 경로를 확인합니다.

1. Compose 서비스와 health 상태 확인
2. 임시 Player `register`와 `login`으로 API HTTP·EF Core trace 생성
3. TCP `Login`과 `PlayerProfile` 패킷으로 Game transaction 생성
4. Elasticsearch에서 API·Game 구조화 로그 검색
5. Elasticsearch APM data stream에서 API·Game transaction 검색

### 사전 조건

- Docker Desktop 실행
- 저장소 루트의 `.env`에 `POSTGRES_PASSWORD`와 `JWT_KEY` 설정
- PowerShell에서 스크립트 실행

스택이 아직 실행 중이 아니라면 빌드와 실행을 포함합니다.

```powershell
.\scripts\observability-smoke.ps1 -StartStack
```

이미 다음 명령으로 스택을 실행했다면 검증만 수행합니다.

```powershell
docker compose `
    -f compose.yaml `
    -f compose.observability.yaml `
    up -d

.\scripts\observability-smoke.ps1
```

스크립트는 Compose에서 현재 공개된 호스트 포트를 자동으로 찾습니다. 원격 주소나 Compose 외부 서비스를 검사할 때만 URI와 포트를 직접 전달합니다.

```powershell
.\scripts\observability-smoke.ps1 `
    -ApiBaseUri "http://localhost:15201" `
    -GamePort 17777 `
    -ElasticsearchUri "http://localhost:19200" `
    -KibanaUri "http://localhost:15601" `
    -LogstashUri "http://localhost:19600" `
    -ApmServerUri "http://localhost:18200"
```

### 성공 기준

마지막에 다음 메시지가 출력되어야 합니다.

```text
[observability-smoke] Success: API/Game logs and APM transactions were collected
```

스크립트는 비밀번호와 JWT를 출력하지 않습니다. 매 실행 시 충돌을 피하기 위해 `obs` 접두사의 임시 Player가 하나 생성되며 로컬 PostgreSQL에 유지됩니다.

Kibana에서는 `http://localhost:5601`에 접속해 다음 서비스로 필터링할 수 있습니다.

- `blue-server-api`
- `blue-server-game`

실패 메시지는 Compose 상태, HTTP endpoint, API 요청, TCP 패킷, Elasticsearch 로그, APM transaction 중 실패한 경계를 구분해 표시합니다.
