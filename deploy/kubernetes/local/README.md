# 로컬 Kubernetes 배포

PostgreSQL, Redis, EF Core Migration, Orleans Silo를 로컬 Kubernetes 클러스터에서 실행하기 위한 매니페스트다. 운영 Azure 환경에서는 PostgreSQL과 Redis를 관리형 서비스로 교체한다.

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

Migration 실행 이미지를 빌드한다.

```powershell
docker build `
    --file src/blueServer.Migrations/Dockerfile `
    --tag blue-server-migrations:local `
    .
```

완료된 Job이 남아 있으면 삭제하고 새 Migration Job을 실행한다.

```powershell
kubectl --namespace blue-server delete job database-migration `
    --ignore-not-found
kubectl apply -f deploy/kubernetes/local/migration.yaml
kubectl --namespace blue-server wait `
    --for=condition=complete `
    job/database-migration `
    --timeout=180s
kubectl --namespace blue-server logs job/database-migration
```

로그에 `Database migrations completed.`가 출력되어야 한다. Migration Job이 실패하면 애플리케이션 Pod를 배포하지 않고 원인을 먼저 해결한다.

Orleans Silo 이미지를 빌드한다.

```powershell
docker build `
    --file src/Orleans/blueServer.Silo/Dockerfile `
    --tag blue-server-silo:local `
    .

.\deploy\kubernetes\local\import-kind-image.ps1 `
    -Image blue-server-silo:local
```

Silo 전용 RBAC과 Deployment를 배포한다.

```powershell
kubectl apply `
    -f deploy/kubernetes/local/configmap.yaml `
    -f deploy/kubernetes/local/silo-rbac.yaml `
    -f deploy/kubernetes/local/silo.yaml

kubectl --namespace blue-server rollout restart `
    deployment/blue-server-silo

kubectl --namespace blue-server rollout status `
    deployment/blue-server-silo `
    --timeout=180s
```

Docker Desktop의 kind 노드는 고정된 로컬 태그를 다시 빌드해도 이전 이미지를 재사용할 수 있다. `import-kind-image.ps1`로 새 이미지를 노드에 넣은 뒤 Deployment를 재시작한다. 운영 배포에서는 이 스크립트 대신 registry와 Git commit SHA 등 변경되지 않는 이미지 태그를 사용한다.

Silo Pod와 로그를 확인한다.

```powershell
kubectl --namespace blue-server get pods `
    --selector app.kubernetes.io/name=blue-server-silo `
    --output=wide

kubectl --namespace blue-server logs `
    --selector app.kubernetes.io/name=blue-server-silo `
    --prefix `
    --tail=100
```

Silo Pod 2개가 모두 `Running`과 `Ready` 상태이고, 로그에 Orleans가 동일한 ClusterId와 ServiceId로 시작된 기록이 있어야 한다.

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
