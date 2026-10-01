# 기능별 API 테스트 케이스

|ID|기능|조건|기대결과|자동화|
|---|---|---|---|---|
|AUTH-01|회원가입|정상 강한 비밀번호|201 + 확인메일|O|
|AUTH-02|로그인|이메일 미확인|401|O|
|AUTH-03|로그인|잘못된 비밀번호|401|O|
|AUTH-04|로그인|5회 실패|423 Lock|O|
|2FA-01|설정|인증 사용자|secret/otpauth 반환|O|
|2FA-02|활성화|올바른 TOTP|2FA ON + recovery codes|O|
|2FA-03|로그인|잘못된 TOTP|401|O|
|2FA-04|로그인|정상 TOTP|Access+Refresh|O|
|2FA-05|복구|복구코드 첫 사용|200|O|
|2FA-06|복구|동일 복구코드 재사용|401|O|
|2FA-07|2차 인증|잘못된 TOTP 5회 후 정상 TOTP|423 Lock (REG-006)|O|
|2FA-08|복구|잘못된 복구코드 5회 후 정상 복구코드|423 Lock (REG-006)|O|
|2FA-09|disable/reset|2FA 해제·초기화 후 기존 Refresh|401 (REG-005)|O|
|2FA-10|enable|2FA 활성화 전 발급된 Refresh|401 (REG-007)|O|
|2FA-11|challenge|challengeToken을 Bearer로 /me·/2fa/status·재발급 호출|401 (REG-008)|O|
|2FA-12|setup→enable|setup 직후 같은 Access Token으로 enable|200 (REG-009)|O|
|AUD-04|감사 로그|로그아웃·비밀번호 변경으로 폐기된 토큰으로 Refresh|`refresh.revoked`(사유), `refresh.reuse` 아님 (REG-010)|O|
|VB6-01|VB6 샘플 모듈|2FA 사용자 ApiLogin → ApiTotp → ApiGet → ApiRefresh → ApiLogout, 실패 경로|11/11 (REG-011)|E2E|
|E2E-01|Web/VB6|`scripts/run-e2e.ps1` 전체 시나리오(가입~잠금)|Web 44/44, VB6 95/95|E2E|
|AUD-01|감사 로그|로그인·2FA·복구 코드·로그아웃·2FA 해제|이벤트 기록 + Detail에 토큰/Secret/복구 코드 없음|O|
|AUD-02|감사 로그|복구 코드 재발급·2FA 초기화|이벤트 기록|O|
|AUD-03|감사 로그|TOTP 5회 실패 후 시도|account.locked 1회 + 2fa.locked/login.locked|O|
|TOK-01|Refresh|정상 토큰|새 Access+Refresh|O|
|TOK-02|Refresh|회전 전 토큰 재사용|401 + family revoke|O|
|TOK-03|Refresh|family revoke 후 최신 토큰|401|O|
|TOK-04|Logout|revoke 후 refresh|401|O|
|SEC-01|Access|SecurityStamp 변경|기존 Access 401|O|
|ADM-01|관리자 2FA reset|일반 사용자|403|O|
|ADM-02|관리자 2FA reset|Admin + 사유 없음|400|추가 권장|
|PWD-01|forgot|존재/미존재 이메일|동일 202 응답|추가 권장|
|PWD-02|reset|정상 토큰|204 + 기존 세션 폐기|추가 권장|
|RATE-01|auth endpoint|허용량 초과|429|별도 작은 limit factory 권장|

## 회귀 규칙
1. 실패 테스트는 원인 분류(코드 결함/테스트 결함/환경 결함).
2. 코드 결함이면 해당 실패를 재현하는 테스트를 먼저 유지한 채 수정.
3. 수정 후 해당 테스트 → AuthFlowTests 전체 → 전체 테스트 순으로 재실행.
4. Refresh 재사용, SecurityStamp, 2FA 복구코드는 보안 회귀 필수 항목으로 삭제 금지.
