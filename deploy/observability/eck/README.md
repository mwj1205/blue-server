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

## 다음 단계

이 단계에서는 수집 Backend만 구성한다. API, Game, Silo의 APM 활성화와 APM Token·CA 인증서 전달은 KAN-69에서 Helm Chart에 추가한다.
