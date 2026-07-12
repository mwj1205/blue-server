# Blue Server Unity Client

Unity에서 게임 서버에 TCP로 로그인한 뒤 플레이어 프로필을 조회하는 최소 클라이언트입니다.

## 실행 준비

1. API `/login` 요청으로 Access Token을 발급합니다.
2. Unity Editor를 실행하기 전에 `BLUE_SERVER_ACCESS_TOKEN` 환경 변수에 Access Token을 설정합니다.
3. API 서버와 TCP 게임 서버를 Development 환경으로 실행합니다.
4. `clients/blueServer.Unity` 프로젝트를 Unity에서 열고 Play Mode를 시작합니다.

Access Token 환경 변수가 존재하면 런타임 bootstrap이 자동으로 다음 흐름을 실행합니다.

```text
TCP 연결 → JWT 로그인 → PlayerProfile 요청 → 프로필 로그 출력
```

환경 변수가 없으면 TCP 연결을 시도하지 않습니다.

기본 TCP 주소는 `127.0.0.1:7777`입니다.
