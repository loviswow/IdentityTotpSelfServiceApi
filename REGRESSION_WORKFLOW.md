# 회귀 테스트 운영 절차

1. 고정 SQL `001_IdentityTotp_v4.sql`을 빈 SQL Server DB에 `sqlcmd -b`로 적용한다.
2. `dotnet restore` → `dotnet build -c Release` → `dotnet test` 순서로 실행한다.
3. 실패하면 TRX에서 최초 실패 케이스와 서버 로그를 확인한다.
4. 원인을 수정할 때 해당 결함을 재현하는 테스트를 먼저 유지/추가한다.
5. 수정 후 실패 케이스만 재실행한 뒤 반드시 전체 테스트를 다시 실행한다.
6. 전체 성공 전에는 Migration 또는 API 계약 변경을 배포하지 않는다.

## 이번 회귀에서 고정한 결함
- 비밀번호 변경 후 기존 Refresh Token이 살아 있던 문제 → 모든 Refresh Token 폐기.
- 동일 Refresh Token 동시 회전 가능성 → Version concurrency token + 충돌 시 family 폐기.
- 공유 테스트 DB에서 고정 이메일 재사용으로 테스트가 순서 의존적이던 문제 → 테스트별 고유 이메일.
- REG-005: 2FA disable/reset 후 기존 Refresh Token이 살아 있던 문제 → SecurityStamp 갱신과 함께 모든 Refresh Token 폐기.
- REG-006: TOTP·복구 코드 2차 인증 실패에 계정 잠금이 적용되지 않던 문제 → 잠금 상태면 423, 복구 코드 실패도 실패 횟수에 포함.
- REG-007: 2FA 활성화 전 비밀번호만으로 받은 Refresh Token으로 `amr=mfa` Access Token을 받을 수 있던 문제 → 활성화 시 모든 Refresh Token 폐기.
- REG-008: 2FA challengeToken을 Bearer Access Token으로 써서 TOTP 없이 API를 호출할 수 있던 문제 → challenge 전용 Audience + `purpose` 클레임 토큰 거부.
- REG-009: 2FA setup이 SecurityStamp를 바꿔 바로 이어지는 enable이 401이던 문제 → 키 저장소에 키만 저장.
- REG-010: 로그아웃·비밀번호 변경 등으로 정상 폐기된 토큰도 `refresh.reuse`로 기록되던 문제 → 회전된 토큰 재사용만 `refresh.reuse`, 나머지는 `refresh.revoked`(사유 포함).
- REG-011: VB6 샘플 `modIdentityApi.bas`의 `JsonBool`/`JsonString` 결함으로 2FA 사용자를 인식하지 못하던 문제 → 파싱 수정, `tools/Vb6SampleCheck`로 E2E에서 검증.
- 최초 실제 빌드에서 발견한 빌드·DB·테스트 인프라 결함 목록은 이관 문서 22장 참고.

## E2E (실제 서버 + Web/VB6 클라이언트)
xUnit 전체 통과 후, API 계약·인증 흐름을 바꿨다면 E2E도 실행한다.

```powershell
.\scripts\run-e2e.ps1 -SqlServer "localhost\SQLEXPRESS" -Database IdentityTotp_Test
```

Web(Node), Web(헤드리스 Chrome), VB6가 모두 통과하고 감사 로그 점검까지 통과해야 성공(종료 코드 0)이다. 시나리오를 바꾸면 `tools/WebTestClient/public/scenario.js`와 `tools/Vb6TestClient/modScenario.bas`를 함께 고친다.

## 주요 조건
AUTH: 가입/미확인 이메일/정상·오류 로그인/잠금/429
TOTP: setup/enable/정상·오류/복구코드 1회성/reset
TOKEN: rotation/reuse/family revoke/logout/concurrent refresh
PASSWORD: 변경/forgot/reset/기존 access·refresh 폐기
ADMIN: 비관리자 403/관리자 reset/사유 필수
DB: migration 재실행(idempotent)/rollback/인덱스·FK·동시성 컬럼
