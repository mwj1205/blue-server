# Blue Server Unity Client

Unity에서 게임 서버에 TCP로 로그인한 뒤 플레이어 프로필과 Mail을 조회하고,
읽음 처리와 보상 수령을 검증하는 최소 클라이언트입니다.

## 실행 준비

1. API 서버와 TCP 게임 서버를 Development 환경으로 실행합니다.
2. API `/login` 요청으로 Access Token을 발급합니다.
3. Unity Editor를 실행하기 전에 `BLUE_SERVER_ACCESS_TOKEN` 환경 변수에 Access Token을 설정합니다.
4. `clients/blueServer.Unity` 프로젝트를 Unity에서 열고 Play Mode를 시작합니다.

Access Token 환경 변수가 존재하면 런타임 bootstrap이 자동으로 다음 흐름을 실행합니다.

```text
TCP 연결
  → JWT 로그인
  → PlayerProfile 요청과 로그 출력
  → Mail 첫 페이지 조회
  → 전체 인벤토리 조회와 Mail/Inventory 화면 표시
```

환경 변수가 없으면 TCP 연결을 시도하지 않습니다.

기본 TCP 주소는 `127.0.0.1:7777`입니다.

## 메일 아이템 부분 수령 → 상자 사용 데모

최신 Game 서버가 필요합니다. 이전 배포 이미지는 아이템 상세 확장/상자 Opcode를 지원하지 않을 수 있습니다.
현재 로컬 임시 서버를 사용하는 경우 API는 `http://localhost:5201`, Game은 `127.0.0.1:17778`입니다.
서버 재시작이나 이 채팅 종료 후에는 실행 여부를 다시 확인합니다.

테스트 계정은 `unity-box-overflow-demo`, 비밀번호는 `local-box-demo-password`입니다.
실제 사용자 비밀번호가 아닌 로컬 데모 전용 값입니다. 최초 계정과 데이터는 준비되어 있습니다.
재구성 시 API `/register`로 계정을 생성하고 `scripts/sql/unity-box-overflow-demo.sql`을
로컬 PostgreSQL에서 실행합니다. 이 SQL은 기존 데이터를 삭제하거나 다시 채우지 않습니다.
직접 넣은 테스트 데이터이므로 실제 InventoryGrantService의 초과 Mail 발송을 검증한 것은 아닙니다.

초기 데이터: Gold 1,000 / Gem 500, 상자 3101~3104 각각 999,990개,
각 상자를 20개씩 첨부한 Mail 4개(30일 만료).

Unity Editor를 저장 후 완전히 종료하고, 저장소 루트 PowerShell에서 실행합니다.

```powershell
$login = Invoke-RestMethod -Method Post -Uri 'http://localhost:5201/login' -ContentType 'application/json' -Body (@{
  nickname = 'unity-box-overflow-demo'
  password = 'local-box-demo-password'
} | ConvertTo-Json)
$env:BLUE_SERVER_ACCESS_TOKEN = $login.accessToken
$env:BLUE_SERVER_GAME_HOST = '127.0.0.1'
$env:BLUE_SERVER_GAME_PORT = '17778'
& 'C:\Program Files\Unity\Hub\Editor\2019.4.20f1\Editor\Unity.exe' -projectPath "$PWD\clients\blueServer.Unity"
$login = $null
```

Kubernetes Service를 7777로 포트포워딩한 경우에는 `BLUE_SERVER_GAME_PORT`를 `7777`로 지정합니다.
환경 변수는 Unity 프로세스 시작 시 전달되므로 이미 열린 Editor에는 반영되지 않습니다.

Play Mode에서 다음 순서로 확인합니다.

오른쪽 고정 패널에서 Gold·Gem과 서버 전체 조회로 확인한 상자 4종의 수량을 볼 수 있습니다.
최초 조회와 단일·일괄 수령/상자 사용 후 전체 인벤토리를 다시 조회합니다.
완성된 최신 목록에 없는 상자는 0개이며, 미조회·조회 실패 상태는 `Unknown`입니다.
Game View가 좁으면 두 패널이 겹치지 않도록 전체 UI를 축소합니다.

1. `Unity box overflow 3101` → `Details`: total 20 / claimed 0 / remaining 20, 만료 시각 확인.
2. `Claim`: 9개만 지급, inventory 999,999 / remaining Mail 11. 수령 완료가 아니며 만료 시각 유지.
3. `Inventory / Box Use` 탭에서 `3101 Gold100`, Use quantity `20` → `Use (new request)`:
   Gold +2,000, 현재 Gold 3,000, inventory 999,979.
4. `Mail` 탭의 같은 메일에서 `Claim`: 잔여 11개 지급, inventory 999,990 / remaining 0. 만료 시각 유지.
5. `Retry last request`: 최초 상자 사용 결과를 복원하며 Gold는 3,000 그대로.
   재전송 결과는 과거 snapshot이지만 오른쪽 수량과 목록은 최신 전체 조회로 갱신한다.
6. 다른 상자도 같은 방식으로 확인. 3102는 Gem +2,000,
   3103/3104는 상자 20개 독립 추첨으로 Gold/Gem 합계 +400~4,000.

### 아이템 일괄 수령

`Mail` 탭의 `Claim All`은 이제 RequestId가 있는 아이템 포함 일괄 수령 요청을 보냅니다.
기존 재화 전용 클라이언트 인터페이스는 호환성을 위해 유지합니다.

- `Claim All`: 새로운 일괄 수령 작업. 완료·부분 수령·공간 부족 Mail 수와 재화/아이템별 지급 합계 표시.
- `Retry Claim All`: 마지막 작업의 같은 ID를 재전송. 최초 결과를 복원하며 추가 지급하지 않음.
- 처리 후 Mail 목록·현재 재화·전체 인벤토리를 재조회. 잔여 수량/만료일은 `Details`로 확인.
- `HasMore`라도 자동 반복하지 않음. 후보가 많거나 공간 부족이면 공간을 확보하고 새 `Claim All` 실행.

초기 데이터가 그대로라면 `Claim All`로 Mail 4개에서 각각 9개씩 받고, 11개씩 남습니다.
다시 새 `Claim All`을 누르면 공간 부족 4개가 표시됩니다. 상자 20개를 사용한 뒤
새 `Claim All`을 누르면 해당 상자의 잔여 11개를 받고 나머지는 공간 부족으로 유지됩니다.
기존 데모 데이터를 이미 사용했다면 결과 건수는 달라집니다. SQL 재실행으로 사용 내역을 초기화하지 않습니다.

목록과 상자 수량은 마지막 전체 조회 시점의 상태이며 다른 클라이언트 변경까지 실시간으로 보장하지 않습니다.
일괄 응답은 지급 합계만 포함하므로 합산해 보유량을 추정하지 않고 전체 조회 결과로 교체합니다.
일괄 응답 조립은 최대 64프레임/256KiB로 제한하며 요청 ID·순번·총 프레임 수를 검증합니다.
모든 프레임을 받을 때까지 요청 lock과 15초 deadline을 유지하므로 다음 응답과 섞이지 않습니다.
송수신 실패/15초 timeout은 연결을 폐기합니다. 결과가 불명확하면 새 ID로 자동 재시도하지 않습니다.
같은 Play 세션 안에서 RequestId를 보관하지만 Editor 재시작을 넘는 영구 보관/자동 재연결은 지원하지 않습니다.
재화 최대치 초과 정책은 미결정이며 현재 서버의 전체 롤백 동작을 유지합니다.

## 자동 검증 범위

Unity Test Runner → EditMode에서 Codec/상태/로컬 가짜 TCP 서버 테스트를 실행합니다.
`ItemUseLiveServerTests`는 별도 초기 데이터가 있는 계정의 JWT를
`BLUE_SERVER_UNITY_LIVE_TEST_TOKEN`, Game 포트를 `BLUE_SERVER_UNITY_LIVE_TEST_PORT`로
제공할 때만 실행합니다. 수동 데모 계정 토큰을 넣으면 데모 데이터를 소비하므로 사용하지 않습니다.
실서버 조건이 없을 때 이 테스트는 Ignore이며, 통과로 취급하지 않습니다.

## 로컬 Kubernetes 연결

`docker-desktop` Kubernetes Context와 `blue-server` Namespace를 기준으로 합니다.

첫 번째 PowerShell에서 API Service를 로컬 `5201` 포트로 연결합니다.

```powershell
kubectl port-forward service/blue-server-api `
  --namespace blue-server `
  5201:80
```

두 번째 PowerShell에서 Game Service를 Unity가 사용하는 `7777` 포트로 연결합니다.

```powershell
kubectl port-forward service/blue-server-game `
  --namespace blue-server `
  7777:7777
```

두 Port Forward Process는 Unity 테스트가 끝날 때까지 실행 상태를 유지해야 합니다.

## Access Token 설정과 Unity 실행

세 번째 PowerShell에서 기존 테스트 Player로 로그인합니다.

```powershell
$nickname = "<TEST_PLAYER_NICKNAME>"
$password = "<TEST_PLAYER_PASSWORD>"

$login = Invoke-RestMethod `
  -Uri "http://localhost:5201/login" `
  -Method Post `
  -ContentType "application/json" `
  -Body (@{
    nickname = $nickname
    password = $password
  } | ConvertTo-Json)

$env:BLUE_SERVER_ACCESS_TOKEN = $login.accessToken
```

Access Token은 Unity Process가 시작될 때 읽습니다. 이미 Unity Editor가 실행 중이면 완전히
종료한 후 같은 PowerShell에서 다시 실행합니다.

```powershell
& "C:\Program Files\Unity\Hub\Editor\2019.4.20f1\Editor\Unity.exe" `
  -projectPath "$PWD\clients\blueServer.Unity"
```

Unity가 열리면 Play Mode를 시작합니다. 별도의 Scene GameObject를 생성할 필요 없이
`PlayerProfileClientBootstrap`이 런타임 객체와 Mail 화면을 자동으로 생성합니다.

테스트가 끝나면 현재 PowerShell에 남은 Access Token을 제거합니다.

```powershell
Remove-Item Env:BLUE_SERVER_ACCESS_TOKEN `
  -ErrorAction SilentlyContinue
```

Access Token을 로그나 저장소 파일에 기록하지 않습니다.

## 전체 인벤토리 TCP 조회 (통신 계층)

`GameTcpClient.GetInventoryAsync(CancellationToken)`는 로그인된 연결에서 전체 인벤토리를
한 번 요청한다. `IInventoryQueryClient`로도 호출할 수 있다. 로그인 후 전체 목록을 불러오며,
`Inventory / Box Use` 탭의 All/Materials/Shards/Consumables/Event 전환은 로컬에서만 필터링한다.
현재 장비 모델은 없으므로 Equipment 카테고리를 임의로 추가하지 않는다.

`Refresh inventory`는 지급 요청을 재전송하지 않고 목록만 다시 조회한다.
조회 실패 시 이전 목록은 `(stale)`로 남기되 현재 보유량으로 취급하지 않고 새 상자 사용 버튼도
비활성화한다. 연결 자체가 종료되었다면 Play Mode를 다시 시작해 재연결해야 한다.
이름/설명/아이콘의 Catalog 표시 연결은 이번 작업 범위 밖이므로 ID와 종류를 표시한다.

응답은 최대 10,000종/33프레임으로 제한하고, RequestId·순번·전체 개수·아이템 값을
검증한 뒤 완성된 읽기 전용 목록만 반환한다. 수신 도중에는 기존 요청 잠금과 총 15초
제한을 유지한다. 프레임 오류·연결 종료·취소 시 부분 목록을 반환하지 않고 연결을 폐기한다.
PlayerNotFound/InventoryTooLarge는 정상적으로 해석된 실패 응답이며 빈 목록과 구분한다.

Unity EditMode의 `InventoryListPacketCodecTests`는 빈/최대 목록과 310/311종 경계,
잘못된 순번·개수·아이템·프레임 길이 및 실패 이후 조립기 재사용 거절을 검증한다.
이는 실제 서버 왕복이나 UI 실행 테스트와는 별도다.

`InventoryInboxStateTests`는 최초/빈 목록 교체, 로딩 중 중복 요청 차단, 실패 후 조회만 재시도,
취소 시 상태 정리, 단일·일괄 수령과 상자 사용 후 전체 조회 결과 반영을 검사한다.
`ItemUseLiveServerTests`는 초기 상태의 전용 계정(상자4종 각999990개와 첨부20개 Mail4건,
Material/CharacterShard/EventCurrency 아이템 각1종)을 준비해야 한다. 기존에 사용한 데모 계정을
초기화하지 말고 새 테스트 계정을 사용한다. 실제 수령/사용/재전송 뒤 최신 인벤토리를 확인한다.

## Mail 화면 검증

현재 Mail 화면은 서버 기능 검증을 위한 IMGUI 화면입니다. 다음 순서로 확인합니다.

1. 첫 페이지 Mail 목록과 `Unread`, `Claimable`, `Claimed`, `Expired` 상태를 확인합니다.
2. 20개를 초과하는 Mail이 있으면 `Load More`로 다음 페이지를 불러옵니다.
3. `Details`에서 본문과 Gold·Gem Attachment를 확인합니다.
4. `Mark as Read` 후 목록과 상세의 읽음 상태가 함께 변경되는지 확인합니다.
5. `Claim` 후 해당 Mail이 수령 상태로 바뀌고 현재 Gold·Gem이 표시되는지 확인합니다.
6. `Claim All` 후 지급 요약과 재조회된 목록을 확인합니다. 공간 부족/부분 수령이면 Mail이 남는 것이 정상입니다.

테스트용 Mail을 DB에 직접 넣는 방식은 Unity의 조회·읽음·수령 흐름만 검증합니다.
실제 Mail 발송 흐름과 중복 방지 검증에는 서버의 `MailDeliveryService`를 사용해야 합니다.
