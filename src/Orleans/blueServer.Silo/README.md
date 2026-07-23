# Blue Server Orleans Silo

Orleans Grain을 실행하는 Silo 프로젝트다.

`Orleans:HostingMode`에 따라 Silo endpoint와 identity를 구성한다.

* `Manual`: 설정 파일의 Silo 이름, advertised host, 포트를 직접 사용
* `Kubernetes`: Pod 이름과 Pod IP를 사용하도록 Orleans Kubernetes hosting 적용

`Orleans:ClusteringMode`에 따라 두 가지 클러스터 구성을 지원한다.

* `Development`: 로컬 직접 실행용 Development clustering
* `Redis`: Docker Compose와 이후 Kubernetes용 Redis membership

## 실행

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=5433;Database=bluearchive;Username=postgres;Password=replace-with-local-password;GSS Encryption Mode=Disable"
dotnet run --project src/Orleans/blueServer.Silo
```

`replace-with-local-password`는 로컬 `.env`의 `POSTGRES_PASSWORD` 값으로 교체한다.
연결 문자열은 저장소에 커밋하지 않고 환경 변수나 별도 secret 저장소에서 주입한다.

기본 포트는 Silo 간 통신 `11111`, Client Gateway `30000`이다.
다른 포트가 필요하면 환경 변수로 재정의한다.

```powershell
$env:Orleans__SiloPort = "11112"
$env:Orleans__GatewayPort = "30001"
dotnet run --project src/Orleans/blueServer.Silo
```

`ClusterId`가 같은 Silo와 Client만 같은 로컬 클러스터에 참여해야 한다.
`ServiceId`는 clustering provider를 변경해도 동일하게 유지한다.

## Redis clustering

Docker Compose에서는 Primary Silo 없이 두 Silo가 Redis membership을 공유한다.
API와 Game도 Redis에서 현재 Gateway 목록을 조회하므로 정적 Gateway 주소를 설정하지 않는다.

직접 Redis 모드를 실행하려면 `ClusteringMode`와 별도의 Orleans Redis 연결 문자열을 설정한다.

```powershell
$env:Orleans__ClusteringMode = "Redis"
$env:ConnectionStrings__OrleansRedis = "localhost:6380,abortConnect=false"
dotnet run --project src/Orleans/blueServer.Silo
```

Redis membership은 클러스터 가용성에 필요한 데이터다. Compose의 Redis는 `redis-data` volume과 AOF `everysec` 설정으로 데이터를 영속화한다.
Redis 장애가 발생하면 기존 Silo가 즉시 모두 종료되는 것은 아니지만, membership 갱신과 새 Silo·Client의 클러스터 참여가 실패할 수 있다.

## Kubernetes hosting

Kubernetes hosting은 membership provider가 아니다. Kubernetes에서도 `ClusteringMode`는 `Redis`를 사용하고, `HostingMode`만 `Kubernetes`로 설정한다.

```text
Orleans__HostingMode=Kubernetes
Orleans__ClusteringMode=Redis
```

Kubernetes hosting은 Pod에서 전달받은 `POD_NAME`, `POD_NAMESPACE`, `POD_IP`, `ORLEANS_SERVICE_ID`, `ORLEANS_CLUSTER_ID`를 사용한다. 이 환경 변수와 Orleans label, ServiceAccount 권한은 Kubernetes 매니페스트 단계에서 구성한다.
