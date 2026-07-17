# 로컬 검증 스크립트

## ELK·Elastic APM 통합 smoke test

`observability-smoke.ps1`은 컨테이너가 실행 중인지만 검사하지 않고 다음 전체 경로를 확인합니다.

1. Compose 서비스와 health 상태 확인
2. 임시 Player `register`와 `login`으로 API HTTP·EF Core trace 생성
3. TCP `Login`과 `PlayerProfile` 패킷으로 Game transaction 생성
4. Elasticsearch에서 API·Game 구조화 로그 검색
5. Elasticsearch APM data stream에서 API·Game transaction 검색

### 사전 조건

- Docker Desktop 실행
- 저장소 루트의 `.env`에 `POSTGRES_PASSWORD`와 `JWT_KEY` 설정
- PowerShell에서 스크립트 실행

스택이 아직 실행 중이 아니라면 빌드와 실행을 포함합니다.

```powershell
.\scripts\observability-smoke.ps1 -StartStack
```

이미 다음 명령으로 스택을 실행했다면 검증만 수행합니다.

```powershell
docker compose `
    -f compose.yaml `
    -f compose.observability.yaml `
    up -d

.\scripts\observability-smoke.ps1
```

스크립트는 Compose에서 현재 공개된 호스트 포트를 자동으로 찾습니다. 원격 주소나 Compose 외부 서비스를 검사할 때만 URI와 포트를 직접 전달합니다.

```powershell
.\scripts\observability-smoke.ps1 `
    -ApiBaseUri "http://localhost:15201" `
    -GamePort 17777 `
    -ElasticsearchUri "http://localhost:19200" `
    -KibanaUri "http://localhost:15601" `
    -LogstashUri "http://localhost:19600" `
    -ApmServerUri "http://localhost:18200"
```

### 성공 기준

마지막에 다음 메시지가 출력되어야 합니다.

```text
[observability-smoke] Success: API/Game logs and APM transactions were collected
```

스크립트는 비밀번호와 JWT를 출력하지 않습니다. 매 실행 시 충돌을 피하기 위해 `obs` 접두사의 임시 Player가 하나 생성되며 로컬 PostgreSQL에 유지됩니다.

Kibana에서는 `http://localhost:5601`에 접속해 다음 서비스로 필터링할 수 있습니다.

- `blue-server-api`
- `blue-server-game`

실패 메시지는 Compose 상태, HTTP endpoint, API 요청, TCP 패킷, Elasticsearch 로그, APM transaction 중 실패한 경계를 구분해 표시합니다.
