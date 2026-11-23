# Game PostgreSQL Integration Test

`RewardGrantService`의 재화 변경과 보상 지급 이력이 같은 PostgreSQL Transaction으로 저장되는지 검증한다.
일반 `dotnet test`에서는 환경 변수가 없으면 건너뛰며, 실제 DB 검증 시에만 연결 문자열을 전달한다.

Catalog 동기화 테스트는 전체 ItemTemplate 상태를 기준으로 동작하므로
`CatalogSynchronizationCollection`의 `DisableParallelization = true`로 다른 Collection과 격리한다.
나머지 테스트의 병렬 실행과 테스트 내부에서 명시적으로 시작하는 동시성 검증은 유지한다.
이 설정은 같은 테스트 실행 프로세스에만 적용된다. 같은 DB를 사용하는 별도 테스트 프로세스나
Catalog 동기화 Job을 동시에 실행하지 않는다.

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

## 재화 상자 사용 서비스 검증

`CurrencyBoxUseServiceIntegrationTests`는 적용된 `AddCurrencyBoxUseRecords` Migration과
Catalog 상자 3101~3104가 있는 PostgreSQL을 사용한다. API/Game 실행은 필요 없다.
일반 DB 테스트와 같은 연결 환경 변수를 설정하고 필터에 해당 클래스 이름을 지정한다.
상자 4종의 소비·재화 지급·사용/변경 이력, 같은 RequestId의 최초 결과 복원,
다른 사용 개수의 충돌, 보유량 부족 시 추첨 미호출, 재화 overflow 시 전체 롤백을 확인한다.
랜덤 보상은 고정된 테스트 난수원으로 검증하며, 생성한 Player와 이력은 삭제하지 않는다.
동시 요청은 저장 직전 Interceptor로 두 요청을 맞춰 검증한다. 같은 ID·같은 payload는
승자의 랜덤 결과로 복원하고, 같은 ID·다른 payload는 충돌로 거절한다. 서로 다른 ID는
한 요청만 커밋하며 패자의 소비·지급·이력이 남지 않는지, 재시도 시 보유량 부족인지 확인한다.
커밋 후 응답 유실 장애 주입은 별도 후속 검증이다.

## 재화 상자 HTTP API 통합 검증

`POST /players/me/currency-boxes/{itemTemplateId}/use`는 JWT 인증이 필요하다.
Player ID와 처리 시각은 서버에서 결정하며, 요청 본문은 다음과 같다.

```json
{
  "quantity": 2,
  "requestId": "dbe8219c-d86d-4f2c-a6de-a7b5cf98094c"
}
```

- `quantity`: 1~999999, `requestId`: 비어 있지 않은 GUID. 재전송은 같은 ID와 같은 개수를 사용한다.
- 200: `requestId`, `itemTemplateId`, `usedQuantity`, `quantityAfter`, `currencyType`(Gold/Gem),
  `grantedAmount`, `currentGold`, `currentGem`, `processedAt`를 반환한다. 완료 재요청은 최초 응답을 복원한다.
- 400: 잘못된 ID/개수/본문, 401: 인증 실패, 404: Player 또는 상자 정의 없음.
- 409: 보유량 부족, DB 아이템 정의 없음/비활성, 멱등성 충돌, 동시 수정 충돌.
  실패 응답의 `code`로 구분한다. `ConcurrencyConflict`는 같은 ID로 재시도하고,
  `IdempotencyConflict`는 기존 요청과 다른 내용을 같은 ID로 보내지 않았는지 확인한다.
- API는 시작 시 내장 Catalog를 한 번 검증/로드한다. DB Migration과 Catalog 동기화는 실행하지 않는다.
  새 정의를 배포할 때는 기존 Migration Job을 먼저 실행해야 한다.

`CurrencyBoxHttpApiIntegrationTests`는 DB 연결 문자열과 `BLUE_SERVER_API_BASE_ADDRESS`,
최신 API 실행이 필요하다. 로그인에는 API의 Redis 연결도 필요하다. 테스트 필터는
`FullyQualifiedName~CurrencyBoxHttpApiIntegrationTests`다.
실제 회원가입/JWT 로그인, 인증 거절, 입력 거절과 DB 무변경, 상자 4종 사용,
완료 응답 복원, 다른 개수의 충돌, 최종 잔액·소비·지급 이력을 확인한다.
랜덤의 정확한 추첨값은 서비스 테스트에서 고정 난수원으로 확인하고,
HTTP에서는 실제 난수원의 합계 범위(2개 사용 시 40~400)를 확인한다.
테스트 Player와 이력은 삭제하지 않는다.

## Mail HTTP API 통합 검증

단일 수령 `POST /players/me/mails/{mailId}/claim`은 JSON 본문의 `requestId`(비어 있지 않은 GUID)가 필요하다.
네트워크 오류 후 재시도에는 같은 ID를 사용하고, 남은 아이템을 다시 수령할 때는 새 ID를 사용한다.
응답의 `status`는 `Claimed`, `PartiallyClaimed`, `NoCapacity`, `AlreadyClaimed`를 구분한다.
부분 수령과 공간 부족도 처리 결과가 저장되므로 HTTP 200이며, `claimedAt`은 완료 전에는 null이다.
`items`는 지급량·잔여량·지급 후 보유량을 반환한다. 같은 ID의 재요청은 최초 결과를 그대로 반환한다.
`wasAlreadyClaimed`는 재요청 여부가 아니라, 최초 처리 당시 이미 완료된 Mail이었는지를 나타낸다.

일괄 수령 `POST /players/me/mails/claim-all`은 본문 없이 호출하면 기존 재화 전용 동작을 유지한다.
아이템도 수령하려면 JSON 본문에 비어 있지 않은 GUID `requestId`를 보낸다. 본문이 있는 요청의
JSON `null`, 잘못된 JSON, 누락·빈 ID는 재화 전용 경로로 처리하지 않는다.
HTTP 응답은 Mail별 `entries` 대신 `items`에 ItemTemplateId별 실제 지급량 합계를 담는다.
각 항목은 `itemTemplateId`, `grantedQuantity`만 포함하며 0 지급 항목은 생략한다.
`claimedMailCount`, `partiallyClaimedMailCount`, `noCapacityMailCount`는 완료·부분 수령·
지급 불가 Mail 수다. 부분 수령과 공간 부족도 HTTP 200이며 Mail별 잔여 상태는 목록/상세를
재조회한다. 내부 Mail별 지급 이력은 유지한다. 이전 entries 응답을 사용한 클라이언트는 수정해야 한다.
같은 RequestId에는 최초 결과를 반환한다. `hasMore`는 처리 한도를 넘는 후보가 있었다는 의미이며,
공간 부족 Mail이 같은 묶음에 계속 포함될 수 있으므로 이 값만으로 요청을 자동 반복하지 않는다.
TCP 일괄 수령도 빈 요청은 기존 재화 전용 동작을 유지한다. RequestId가 있는 요청은
아래 TCP 계약의 분할 응답을 사용한다.

HTTP 테스트는 인증 흐름 외에 부분 수령·재요청·일괄 수령 제외·입력 오류·만료일 보존을 확인한다.

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

`MailFlow_PartialItemClaimPreservesRemainderAndReplaysRequest`는 아이템 999,000개 보유 상태에서
아이템 1,000개와 Gold 100이 첨부된 Mail을 실제 TCP로 수령한다. 최초 999개 지급·1개 보존,
같은 RequestId 결과 복원, 공간 부족, 공간 확보 후 새 RequestId 최종 수령을 확인한다.
공간 확보는 테스트가 DB에서 아이템 1개를 직접 차감해 준비한다. 아이템 사용 API 검증은 아니다.
기존 RequestId 없는 요청의 아이템 Mail 거절과 완료 후 최초 요청 결과 복원도 확인한다.
마지막으로 원래 Mail 정보·만료일·읽음 시각 보존, 재화 중복 지급 방지, 수령 기록과 인벤토리를 검사한다.
테스트 Player·ItemTemplate·Mail·변경/수령 이력은 삭제하지 않는다.

### TCP 아이템 일괄 수령 계약

`MailFlow_ClaimAllReplaysSummaryAfterReconnectWithoutDoubleGrant`는 같은 아이템의 Mail 100건을
104바이트의 한 프레임 요약으로 받는다. 연결을 닫은 뒤 재로그인과 같은 RequestId 재요청으로
최초 요약과 반복 요청 본문을 비교한다. DB의 단일 일괄 기록과 100개
개별 기록, 지급량·잔여량·만료일·원문·읽음 시각을 검사한다. 테스트 데이터는 삭제하지 않는다.
재연결 후 지급 결과 복원을 검증하며, 응답 유실이나 서버 측 네트워크 장애를 주입하는 테스트는 아니다.
재조립 코드는 크기·조각 수를 제한한 테스트 전용 구현이고 Unity나 범용 재조립기에 적용한 것은 아니다.

- `MailClaimAll`(23)의 기존 빈 payload는 재화 전용이다. 확장 요청은 `ushort` 길이와
  GUID D 형식 문자열 36바이트로 된 RequestId를 보낸다. 빈 GUID 또는 다른 길이는 거절한다.
- 기존 요청의 `MailClaimAllResult`(24) 형식은 그대로다. 확장 요청의 응답은 동일 opcode를
  쓰며, 각 프레임은 `[Size:ushort][Opcode:ushort][RequestId:string][PartIndex:int][PartCount:int]`
  다음에 본문 조각을 담는다. 순번은 0부터 시작하며 헤더를 포함해 최대 4,096바이트다.
- 본문 조각을 모두 순서대로 이어 붙인 후 읽는다. 요약은 `Status:int`, `ProcessedAt` 유무와
  선택적 Unix milliseconds(long), `ClaimedMailCount`, `GrantedGold`, `GrantedGem`,
  `CurrentGold`, `CurrentGem`(각 int), `HasMore:bool`, `PartiallyClaimedMailCount:int`,
  `NoCapacityMailCount:int`, `ItemCount:int` 순서다.
- 아이템 항목은 `ItemTemplateId:int`, `GrantedQuantity:int`다. 실제 지급량을 ID별로 합산하며
  0 지급 항목을 생략한다. Mail별 잔여량은 상세 조회로 확인한다.
  이전 Mail별 응답 본문과 호환되지 않으므로 클라이언트 파서를 변경해야 한다.
  본문은 처리 시각이 있으면 46 + 8 × 아이템 종류 수 바이트다. 500종은 한 프레임,
  501종부터 기존 4KB 분할을 사용한다. 요약이 항상 한 프레임이라는 보장은 없다.
- 일괄 상태 코드는 기존 0~4를 유지하며 `Processed=5`, `ItemTemplateNotFound=6`,
  `ItemTemplateInactive=7`을 추가한다. Mail별 상태는 응답에 포함하지 않는다.
- 프레임을 모두 받기 전에 연결이 끊기면 미완성 응답은 폐기하고 같은 RequestId로 재시도한다.
  분할 전송은 이미 커밋된 한 일괄 결과를 전달하는 것이며, 각 프레임마다 보상을 지급하지 않는다.
  `HasMore`는 다른 후보의 존재를 뜻하며 응답의 남은 프레임 수와 무관하다.
