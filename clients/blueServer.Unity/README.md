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
  → Mail 화면 생성
```

환경 변수가 없으면 TCP 연결을 시도하지 않습니다.

기본 TCP 주소는 `127.0.0.1:7777`입니다.

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

## Mail 화면 검증

현재 Mail 화면은 서버 기능 검증을 위한 IMGUI 화면입니다. 다음 순서로 확인합니다.

1. 첫 페이지 Mail 목록과 `Unread`, `Claimable`, `Claimed`, `Expired` 상태를 확인합니다.
2. 20개를 초과하는 Mail이 있으면 `Load More`로 다음 페이지를 불러옵니다.
3. `Details`에서 본문과 Gold·Gem Attachment를 확인합니다.
4. `Mark as Read` 후 목록과 상세의 읽음 상태가 함께 변경되는지 확인합니다.
5. `Claim` 후 해당 Mail이 수령 상태로 바뀌고 현재 Gold·Gem이 표시되는지 확인합니다.
6. `Claim All` 후 목록이 자동으로 재조회되고 남은 수령 가능 Mail이 없는지 확인합니다.

테스트용 Mail을 DB에 직접 넣는 방식은 Unity의 조회·읽음·수령 흐름만 검증합니다.
실제 Mail 발송 흐름과 중복 방지 검증에는 서버의 `MailDeliveryService`를 사용해야 합니다.
