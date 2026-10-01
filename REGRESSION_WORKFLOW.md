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

## 주요 조건
AUTH: 가입/미확인 이메일/정상·오류 로그인/잠금/429
TOTP: setup/enable/정상·오류/복구코드 1회성/reset
TOKEN: rotation/reuse/family revoke/logout/concurrent refresh
PASSWORD: 변경/forgot/reset/기존 access·refresh 폐기
ADMIN: 비관리자 403/관리자 reset/사유 필수
DB: migration 재실행(idempotent)/rollback/인덱스·FK·동시성 컬럼
