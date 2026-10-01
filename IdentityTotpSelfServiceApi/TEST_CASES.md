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
