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
|E2E-01|Web/VB6|`scripts/run-e2e.ps1` 전체 시나리오(가입~QR~세션~관리자~잠금)|Web 53/53, VB6 141/141, VB6 샘플 12/12|E2E|
|AUD-01|감사 로그|로그인·2FA·복구 코드·로그아웃·2FA 해제|이벤트 기록 + Detail에 토큰/Secret/복구 코드 없음|O|
|AUD-02|감사 로그|복구 코드 재발급·2FA 초기화|이벤트 기록|O|
|AUD-03|감사 로그|TOTP 5회 실패 후 시도|account.locked 1회 + 2fa.locked/login.locked|O|
|TOK-01|Refresh|정상 토큰|새 Access+Refresh|O|
|TOK-02|Refresh|회전 전 토큰 재사용|401 + family revoke|O|
|TOK-03|Refresh|family revoke 후 최신 토큰|401|O|
|TOK-04|Logout|revoke 후 refresh|401|O|
|SEC-01|Access|SecurityStamp 변경|기존 Access 401|O|
|ADM-01|관리자 2FA reset|일반 사용자|403|O|
|ADM-02|관리자 2FA reset|Admin + 사유 없음·공백·500자 초과|400|O|
|ADM-03|관리자 API|Admin이지만 비밀번호만으로 로그인(amr=pwd)|403 `mfa-required` + `admin.denied`|O|
|ADM-04|관리자 API|일반 사용자|403 `not-admin` + `admin.denied`|O|
|ADM-05|관리자 API|MFA 로그인 후 Admin 역할 회수, 기존 토큰으로 호출|403 (역할은 요청마다 DB에서 확인)|O|
|ADM-06|관리자 2FA reset|정상|204 + `admin.2fa.reset`(ActorUserId=관리자, Detail=사유)|O|
|ADM-07|관리자 2FA reset|자기 계정|400|O|
|ADM-08|감사 로그 조회|userId 필터 / limit 501|해당 사용자 기록만 / 400. 조회도 `admin.audit.read`|O|
|ADM-09|관리자 세션|대상 세션 조회 → 전체 로그아웃(사유 필수)|대상 Access/Refresh 401 + `admin.sessions.revoke-all`|O|
|SESS-01|세션 목록|기기 2대 로그인, 한 기기 Refresh|2개, current 표시, 회전 후에도 같은 세션|O|
|SESS-02|세션 목록|응답 본문|Refresh Token 원문·해시 없음|O|
|SESS-03|특정 기기 로그아웃|`DELETE /sessions/{id}`|그 기기 Access·Refresh 즉시 401, 다른 기기 유지, `refresh.revoked`(session-revoke)|O|
|SESS-04|특정 기기 로그아웃|다른 사용자 세션 ID·없는 ID|404|O|
|SESS-05|다른 기기 로그아웃|revoke-others|현재 세션만 남음|O|
|SESS-06|전체 기기 로그아웃|revoke-all|현재 기기 포함 모든 Access/Refresh 401|O|
|SESS-07|로그아웃|revoke 후 그 세션의 Access|401 (sid 세션 확인)|O|
|SESS-08|재사용 탐지|family 폐기 후 그 세션의 Access|401|O|
|SESS-09|deviceName|2FA 로그인 시 전달, 300자|세션에 128자로 저장|O|
|SESS-10|만료 토큰 정리|보존 기간 지난 만료 토큰 / 보존 기간 내 / 활성|앞의 것만 삭제. 정리 작업이 호스트에 등록됨|O|
|MAIL-01|SMTP 발송|`SmtpEmailSender` → 테스트 SMTP 서버|AUTH PLAIN, From, text/plain 전달|O|
|MAIL-02|SMTP 발송|회원가입 → SMTP로 받은 확인 메일의 토큰으로 confirm-email|204 후 로그인 200|O|
|MAIL-03|SMTP 발송|첫 시도 451(일시 장애)|재시도로 전달|O|
|MAIL-04|SMTP 장애|SMTP 서버 다운 상태에서 register/forgot|201/202, 응답이 SMTP를 기다리지 않음, 최종 실패 시 `email.failed`|O|
|MAIL-05|SMTP 설정|Host만 있고 FromAddress 없음|기동 실패|O|
|MAIL-06|SMTP 설정|`RetryDelaysSeconds` 설정|설정값만 사용(기본값 뒤에 덧붙지 않음)|O|
|REG-012|감사 로그|512자 초과 User-Agent로 로그인(SQL Server)|200 (이전에는 감사 로그 저장 실패로 500)|O|
|REG-013|폐기 경쟁|로그아웃·세션 폐기·전체 로그아웃·비밀번호 변경 저장 직전에 같은 토큰이 회전됨|204, 회전으로 생긴 토큰도 401 (이전에는 500 + 아무것도 폐기 안 됨)|O|
|REG-014|2차 인증|공백 복구 코드 / 공백 TOTP 코드|400, 잠금 실패 횟수 증가 없음 (이전에는 복구 코드 500)|O|
|SESS-11|만료 토큰 정리|살아 있는 세션의 오래된 회전 토큰|지우지 않음, 재사용 시 `refresh.reuse` + family 폐기|O|
|MAIL-07|SMTP 영구 오류|수신자 550|재시도 없이 `email.failed`|O|
|QR-01|등록 QR|setup 후 png/bmp/svg|200, 형식별 시그니처, `Cache-Control: no-store`|O|
|QR-02|등록 QR|setup 전 / 2FA 활성 후|400 / 409|O|
|QR-03|등록 QR|토큰 없음 / format=gif|401 / 400|O|
|QR-04|등록 QR|VB6: bmp 다운로드 → `LoadPicture`, Web: png·bmp|열림 (E2E)|E2E|
|PWD-01|forgot|존재/미존재 이메일|동일 202 응답|E2E|
|PWD-02|reset|정상 토큰|204 + 기존 세션 폐기|E2E|
|RATE-01|auth endpoint|허용량 초과|429|O (`LowRateLimitApiFactory`)|

## 회귀 규칙
1. 실패 테스트는 원인 분류(코드 결함/테스트 결함/환경 결함).
2. 코드 결함이면 해당 실패를 재현하는 테스트를 먼저 유지한 채 수정.
3. 수정 후 해당 테스트 → AuthFlowTests 전체 → 전체 테스트 순으로 재실행.
4. Refresh 재사용, SecurityStamp, 2FA 복구코드는 보안 회귀 필수 항목으로 삭제 금지.
