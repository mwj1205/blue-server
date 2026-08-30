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
