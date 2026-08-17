# Azure AKS ECK 관측 환경

Azure AKS에서 ECK Operator가 Elasticsearch, Kibana, APM Server의 생성과 TLS 인증서 구성을 관리한다. Application은 `blue-dev`, Elastic Stack은 `observability`, ECK Operator는 `elastic-system` Namespace로 분리한다.

## 고정 Version

| 구성 요소 | Version | 선택 근거 |
| --- | --- | --- |
| Kubernetes | `1.35.x` | ECK 3.4 안정판의 공식 지원 범위 |
| ECK Operator | `3.4.1` | 현재 안정판을 재현 가능하도록 고정 |
| Elastic Stack | `9.4.2` | Elasticsearch, Kibana, APM Server Version 통일 |

Operator와 Stack Version은 독립적이다. Version을 변경할 때는 ECK 지원 범위와 Breaking Change를 먼저 확인하고 한 번에 하나씩 Upgrade한다.

## 학습용 Resource 범위

| 구성 요소 | Replica | Memory request / limit | Storage |
| --- | ---: | ---: | ---: |
| Elasticsearch | 1 | `2Gi` / `2Gi` | `managed-csi` 10Gi |
| Kibana | 1 | `1Gi` / `2Gi` | 없음 |
| APM Server | 1 | `512Mi` / `1Gi` | 없음 |

단일 Elasticsearch Node이므로 고가용성이나 운영 장애 대응을 보장하지 않는다. `volumeClaimDeletePolicy`는 학습 환경 정리 시 PVC를 함께 제거하도록 `DeleteOnScaledownAndClusterDeletion`으로 설정한다.

## 설치 전 확인

현재 Context, Kubernetes Version, Azure Disk StorageClass를 확인한다.

```powershell
kubectl config current-context
kubectl version
kubectl get storageclass managed-csi
```

대상은 `aks-blue-server-dev`, Kubernetes는 `1.35.x`, `managed-csi`는 존재하는 상태여야 한다.

## ECK Operator 설치

Elastic Helm Repository를 등록하고 ECK Operator Version을 고정하여 설치한다.

```powershell
helm repo add elastic https://helm.elastic.co
helm repo update

helm upgrade --install elastic-operator elastic/eck-operator `
  --namespace elastic-system `
  --create-namespace `
  --version 3.4.1 `
  --wait `
  --timeout 5m
```

ECK CRD는 Cluster 전체 Resource이므로 Operator 설치에는 Cluster 관리자 권한이 필요하다. CRD를 삭제하면 Cluster 안의 모든 ECK Custom Resource가 함께 삭제될 수 있으므로 일반 Upgrade 과정에서는 CRD를 직접 삭제하지 않는다.

Operator 상태를 확인한다.

```powershell
kubectl --namespace elastic-system get pods
kubectl --namespace elastic-system logs statefulset/elastic-operator --tail=100
```

## Elastic Stack 적용

Operator가 Ready인 뒤 별도 Namespace와 Custom Resource를 적용한다.

```powershell
kubectl apply -f deploy/observability/eck/namespace.yaml
kubectl apply -f deploy/observability/eck/elastic-stack.yaml
```

ECK가 Custom Resource를 감지하여 StatefulSet, Deployment, Service, Secret, TLS 인증서를 생성한다. `elasticsearchRef`와 `kibanaRef`를 통해 Component 간 주소와 인증 설정이 자동으로 연결된다.

```powershell
kubectl --namespace observability get elasticsearch,kibana,apmserver
kubectl --namespace observability get pods,services,persistentvolumeclaims
```

Elasticsearch, Kibana, APM Server의 Health가 `green`이고 각 Pod가 `Running`과 `Ready` 상태가 되어야 한다.

## Kibana 접속

ECK가 생성한 `elastic` 사용자 Password를 Process 안에서만 복호화한다.

```powershell
$encodedPassword = kubectl get secret blue-server-es-elastic-user `
  --namespace observability `
  --output jsonpath="{.data.elastic}"

$elasticPassword = [System.Text.Encoding]::UTF8.GetString(
  [System.Convert]::FromBase64String($encodedPassword)
)

$elasticPassword
```

Kibana는 Public Service로 노출하지 않고 로컬 Port Forward로 접속한다.

```powershell
kubectl port-forward `
  --namespace observability `
  service/blue-server-kb-http `
  15601:5601
```

브라우저에서 `https://localhost:15601`로 접속하고 사용자 이름 `elastic`과 위 Password를 사용한다. ECK 자체 서명 인증서를 사용하므로 개발 브라우저에서 인증서 경고가 표시될 수 있다.

## APM Agent 인증정보 전달

Kubernetes Secret은 다른 Namespace에서 직접 참조할 수 없다. ECK가 `observability` Namespace에 생성한 APM Secret Token과 TLS 인증서를 `blue-dev` Namespace의 애플리케이션 전용 Secret으로 복제한다.

먼저 대상 Namespace를 준비한다.

```powershell
kubectl create namespace blue-dev `
  --dry-run=client `
  --output yaml |
  kubectl apply --server-side --filename -
```

Secret 값은 화면이나 명령행 인자에 출력하지 않고 Process 안에서 Base64 상태로 전달한다.

```powershell
$apmTokenSecret = kubectl get secret blue-server-apm-token `
  --namespace observability `
  --output json | ConvertFrom-Json

$apmCertificateSecret = kubectl get secret blue-server-apm-http-certs-public `
  --namespace observability `
  --output json | ConvertFrom-Json

$agentSecret = [ordered]@{
  apiVersion = "v1"
  kind = "Secret"
  metadata = [ordered]@{
    name = "blue-server-apm-agent"
    namespace = "blue-dev"
  }
  type = "Opaque"
  data = [ordered]@{
    "secret-token" = $apmTokenSecret.data."secret-token"
    "tls.crt" = $apmCertificateSecret.data."tls.crt"
  }
}

$agentSecret |
  ConvertTo-Json -Depth 5 |
  kubectl apply `
    --server-side `
    --field-manager blue-server-apm-bootstrap `
    --filename -
```

복제된 Secret의 값은 출력하지 않고 Key 존재 여부만 확인한다.

```powershell
$agentSecretMetadata = kubectl get secret blue-server-apm-agent `
  --namespace blue-dev `
  --output json | ConvertFrom-Json

$agentSecretMetadata.data.PSObject.Properties.Name
```

출력에는 `secret-token`과 `tls.crt`가 있어야 한다. ECK Stack을 다시 생성하여 Token이나 인증서가 변경되면 이 복제를 다시 실행하고 API·Game·Silo Pod를 재시작해야 한다. Token은 환경변수로 주입되므로 실행 중인 Pod에는 자동 반영되지 않는다.

## Helm APM 설정

`values-azure.yaml`은 다음 연결 정보만 관리한다.

- APM Server: `https://blue-server-apm-http.observability.svc:8200`
- Agent Environment: `azure-dev`
- Secret 이름과 Token·인증서 Key 이름
- Transaction Sample Rate와 OpenTelemetry Bridge 설정

실제 Token과 인증서 내용은 Git이나 Helm Release 값에 저장하지 않는다. API·Game·Silo는 같은 APM Server 설정을 공유하되 서로 다른 `ELASTIC_APM_SERVICE_NAME`을 사용하고, Pod 이름을 `ELASTIC_APM_SERVICE_NODE_NAME`으로 전달한다.

ECK 자체 서명 인증서 검증을 끄는 대신 `tls.crt`를 각 Pod의 `/etc/elastic-apm/certs/tls.crt`에 Read-only로 Mount하고 `ELASTIC_APM_SERVER_CERT`로 지정한다.

## 다음 단계

애플리케이션 Namespace에 APM Secret과 기존 Database·Redis·JWT Secret을 준비한 뒤 Helm Release를 설치한다. API, Game, Silo가 APM Server로 전송한 HTTP·TCP·Orleans Trace와 Pod Instance를 Kibana에서 확인한다.
