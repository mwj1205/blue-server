# Blue Server Orleans Silo

Orleans 연결 기반을 확인하기 위한 단일 로컬 Silo다.

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
`ServiceId`는 이후 PostgreSQL clustering과 persistence를 추가해도 동일하게 유지한다.
