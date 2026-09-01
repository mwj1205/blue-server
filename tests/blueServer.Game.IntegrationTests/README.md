# Game PostgreSQL Integration Test

`RewardGrantService`의 재화 변경과 보상 지급 이력이 같은 PostgreSQL Transaction으로 저장되는지 검증한다.
일반 `dotnet test`에서는 환경 변수가 없으면 건너뛰며, 실제 DB 검증 시에만 연결 문자열을 전달한다.

## 로컬 Kubernetes 실행

첫 번째 PowerShell에서 PostgreSQL Service를 로컬 포트로 전달한다.

```powershell
kubectl port-forward statefulset/postgres `
  --namespace blue-server `
  15433:5432
```

두 번째 PowerShell에서 Kubernetes Secret을 출력하지 않고 Test Process에만 전달한다.

```powershell
$encodedPassword = kubectl get secret blue-server-secrets `
  --namespace blue-server `
  --output jsonpath='{.data.POSTGRES_PASSWORD}'

$password = [Text.Encoding]::UTF8.GetString(
  [Convert]::FromBase64String($encodedPassword))

try {
  $env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING = `
    "Host=127.0.0.1;Port=15433;Database=bluearchive;Username=postgres;Password=$password;GSS Encryption Mode=Disable"

  dotnet test `
    .\tests\blueServer.Game.IntegrationTests\blueServer.Game.IntegrationTests.csproj `
    --no-restore
}
finally {
  Remove-Item Env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING `
    -ErrorAction SilentlyContinue

  $password = $null
  $encodedPassword = $null
}
```

테스트는 `reward-integration-{GUID}` 형식의 고유한 Player를 생성하고 다음 동작을 검증한다.
생성한 Player와 지급 이력은 테스트 후에도 PostgreSQL에 유지되므로 DB에서 직접 확인할 수 있다.

- 최초 지급 시 Gold·Gem 변경
- 동일 Request ID와 payload 재시도 시 중복 지급 방지
- 동일 Request ID에 다른 payload 사용 시 멱등성 충돌
- `RewardGrantRecords`, `RewardGrantItems` 지급 snapshot 저장

## Mail HTTP API 통합 검증

Mail HTTP API는 배포된 API의 인증 Pipeline과 PostgreSQL을 함께 사용한다. 첫 번째
PowerShell에서 현재 코드를 API Image로 다시 빌드하고 Kubernetes Node에 적재한
뒤 Deployment를 재시작한다.

```powershell
docker build `
  --file src/blueServer.Api/Dockerfile `
  --tag blue-server-api:local `
  .

.\deploy\kubernetes\local\import-kind-image.ps1 `
  -Image blue-server-api:local

kubectl rollout restart deployment/blue-server-api `
  --namespace blue-server

kubectl rollout status deployment/blue-server-api `
  --namespace blue-server `
  --timeout=180s
```

API Service와 PostgreSQL Service를 각각 포트포워드한다.

```powershell
kubectl port-forward service/blue-server-api `
  --namespace blue-server `
  15201:80
```

```powershell
kubectl port-forward statefulset/postgres `
  --namespace blue-server `
  15433:5432
```

별도 PowerShell에서 Secret을 Test Process 환경 변수로만 전달하고 Mail HTTP
시나리오만 실행한다.

```powershell
$encodedPassword = kubectl get secret blue-server-secrets `
  --namespace blue-server `
  --output jsonpath='{.data.POSTGRES_PASSWORD}'

$password = [Text.Encoding]::UTF8.GetString(
  [Convert]::FromBase64String($encodedPassword))

try {
  $env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING = `
    "Host=127.0.0.1;Port=15433;Database=bluearchive;Username=postgres;Password=$password;GSS Encryption Mode=Disable"

  $env:BLUE_SERVER_API_BASE_ADDRESS = `
    "http://127.0.0.1:15201"

  dotnet test `
    .\tests\blueServer.Game.IntegrationTests\blueServer.Game.IntegrationTests.csproj `
    --no-restore `
    --filter "FullyQualifiedName~MailHttpApiIntegrationTests"
}
finally {
  Remove-Item Env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING `
    -ErrorAction SilentlyContinue

  Remove-Item Env:BLUE_SERVER_API_BASE_ADDRESS `
    -ErrorAction SilentlyContinue

  $password = $null
  $encodedPassword = $null
}
```

테스트는 회원가입·로그인으로 실제 JWT를 발급한 뒤 Mail 목록·상세·읽음·개별
수령·일괄 수령과 재요청을 검증한다. 생성한 Player, Mail과 지급 이력은 테스트
후에도 PostgreSQL에 유지한다.

## Mail TCP Round-trip 통합 검증

최신 API와 Game Image, `AddMailDeliverySources` Migration이 적용된 로컬
Kubernetes Helm Release를 사용한다. 각각 별도의 PowerShell에서 API, Game,
PostgreSQL Service를 포트포워드한다.

```powershell
kubectl port-forward service/blue-server-api `
  --namespace blue-server `
  15201:80
```

```powershell
kubectl port-forward service/blue-server-game `
  --namespace blue-server `
  17777:7777
```

```powershell
kubectl port-forward statefulset/postgres `
  --namespace blue-server `
  15433:5432
```

네 번째 PowerShell에서 Kubernetes Secret을 Test Process에만 전달하고 Mail TCP
시나리오를 실행한다.

```powershell
$encodedPassword = kubectl get secret blue-server-secrets `
  --namespace blue-server `
  --output jsonpath='{.data.POSTGRES_PASSWORD}'

$password = [Text.Encoding]::UTF8.GetString(
  [Convert]::FromBase64String($encodedPassword))

try {
  $env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING = `
    "Host=127.0.0.1;Port=15433;Database=bluearchive;Username=postgres;Password=$password;GSS Encryption Mode=Disable"

  $env:BLUE_SERVER_API_BASE_ADDRESS = `
    "http://127.0.0.1:15201"

  $env:BLUE_SERVER_GAME_HOST = "127.0.0.1"
  $env:BLUE_SERVER_GAME_PORT = "17777"

  dotnet test `
    .\tests\blueServer.Game.IntegrationTests\blueServer.Game.IntegrationTests.csproj `
    --no-restore `
    --filter "FullyQualifiedName~MailTcpRoundTripIntegrationTests"
}
finally {
  Remove-Item Env:BLUE_SERVER_INTEGRATION_CONNECTION_STRING `
    -ErrorAction SilentlyContinue

  Remove-Item Env:BLUE_SERVER_API_BASE_ADDRESS `
    -ErrorAction SilentlyContinue

  Remove-Item Env:BLUE_SERVER_GAME_HOST `
    -ErrorAction SilentlyContinue

  Remove-Item Env:BLUE_SERVER_GAME_PORT `
    -ErrorAction SilentlyContinue

  $password = $null
  $encodedPassword = $null
}
```

테스트는 실제 API 회원가입·로그인으로 JWT를 발급하고 `MailDeliveryService`로
검증 Mail을 생성한다. 이후 실제 Game TCP Session에서 로그인, 목록 Pagination,
상세 Attachment, 읽음 처리와 최초 `ReadAt` 보존, 개별·일괄 수령과 재요청의
중복 지급 방지, 다른 Player Mail 비노출을 검증한다. 마지막으로 PostgreSQL의
Mail 상태와 Player 재화·지급 이력을 확인하며, 생성한 데이터는 테스트 후에도
유지한다.
