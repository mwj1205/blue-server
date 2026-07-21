# 로컬 Kubernetes 데이터 계층

PostgreSQL과 Redis를 로컬 Kubernetes 클러스터에서 실행하기 위한 매니페스트다. 운영 Azure 환경에서는 관리형 PostgreSQL과 Redis로 교체한다.

## 사전 조건

`kubectl config current-context`에서 Docker Desktop 등 로컬 클러스터가 선택되어 있어야 한다.

```powershell
kubectl config current-context
```

## 배포

Namespace를 먼저 생성한다.

```powershell
kubectl apply -f deploy/kubernetes/local/namespace.yaml
```

PostgreSQL 비밀번호는 매니페스트에 저장하지 않고 Secret으로 주입한다. `Read-Host`에 로컬 `.env`의 `POSTGRES_PASSWORD` 값을 입력한다.

```powershell
$postgresPassword = Read-Host "POSTGRES_PASSWORD"
$secretYaml = kubectl create secret generic blue-server-secrets `
    --namespace blue-server `
    --from-literal="POSTGRES_PASSWORD=$postgresPassword" `
    --dry-run=client `
    --output=yaml
$secretYaml | kubectl apply -f -
```

ConfigMap과 StatefulSet을 배포한다.

```powershell
kubectl apply `
    -f deploy/kubernetes/local/configmap.yaml `
    -f deploy/kubernetes/local/postgres.yaml `
    -f deploy/kubernetes/local/redis.yaml
```

## 검증

Pod와 PVC가 준비될 때까지 기다린다.

```powershell
kubectl --namespace blue-server rollout status statefulset/postgres
kubectl --namespace blue-server rollout status statefulset/redis
kubectl --namespace blue-server get pods,services,persistentvolumeclaims
```

각 서버에 직접 명령을 보내 연결 가능 상태를 확인한다.

```powershell
kubectl --namespace blue-server exec statefulset/postgres -- `
    pg_isready -U postgres -d bluearchive
kubectl --namespace blue-server exec statefulset/redis -- `
    redis-cli ping
```

PostgreSQL은 `accepting connections`, Redis는 `PONG`을 반환해야 한다.
