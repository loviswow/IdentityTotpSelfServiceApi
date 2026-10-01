# Identity TOTP Self-Service REST API

ASP.NET Core Identity의 내장 AuthenticatorTokenProvider를 사용한 TOTP 2FA 예제입니다.

## 준비

1. `appsettings.json`의 SQL Server 연결 문자열을 수정합니다.
2. `Jwt:Key`를 운영용 강력한 랜덤 비밀키로 교체합니다. 소스 저장소에 운영 키를 커밋하지 마십시오.
3. EF 도구가 없다면 설치:
   `dotnet tool install --global dotnet-ef`
4. DB 생성:
   `dotnet ef migrations add InitialIdentity`
   `dotnet ef database update`
5. 실행:
   `dotnet run`

## REST 흐름

### 가입
POST `/api/auth/register`

```json
{ "email":"user@example.com", "password":"Strong!Pass123" }
```

### 최초 로그인
POST `/api/auth/login`

2FA 미설정이면 accessToken 반환.

### 2FA 등록
Bearer access token으로:

1. POST `/api/account/2fa/setup`
   - `sharedKey`, `authenticatorUri` 반환
   - Authenticator 앱에 수동키를 입력하거나 클라이언트에서 URI를 QR로 표시
2. 앱에서 나온 6자리 코드로 POST `/api/account/2fa/enable`

```json
{ "code":"123456" }
```

성공 시 recoveryCodes가 **한 번** 반환됩니다. 사용자가 안전한 장소에 저장해야 합니다.

### 2FA가 켜진 뒤 로그인
1. POST `/api/auth/login`
   - `requiresTwoFactor=true`
   - `challengeToken` 반환
2. POST `/api/auth/2fa`

```json
{
  "challengeToken":"...",
  "code":"123456"
}
```

성공 후 실제 accessToken 반환.

### 복구 코드 로그인
POST `/api/auth/2fa/recovery`

```json
{
  "challengeToken":"...",
  "recoveryCode":"xxxxx-xxxxx"
}
```

복구 코드는 일회용입니다.

### Self-service
- GET `/api/account/2fa/status`
- POST `/api/account/2fa/setup`
- POST `/api/account/2fa/enable`
- POST `/api/account/2fa/disable`
- POST `/api/account/2fa/reset`
- POST `/api/account/2fa/recovery-codes/regenerate`
- GET `/api/account/me`
- POST `/api/account/change-password`

## 운영 적용 전에 추가 권장
- Refresh token rotation + 서버측 revoke 저장소
- 이메일 확인 / 비밀번호 분실 재설정
- 중요 작업에 step-up 인증
- 감사 로그
- API rate limiting
- JWT signing key를 Key Vault/HSM/환경변수 등 외부 secret store로 이동
- 프록시 사용 시 forwarded headers 및 HTTPS/HSTS 설정
- NTP 시간 동기화
- CORS allow-list

## v2 운영형 확장
- Access JWT 15분 + Refresh Token 14일
- Refresh Token rotation 및 reuse detection(token family revoke)
- Refresh Token DB에는 SHA-256 hash만 저장
- SecurityStamp를 JWT에 포함하고 매 요청 DB와 비교하여 즉시 revoke 지원
- 이메일 확인 / 비밀번호 찾기·재설정 REST API
- 관리자 2FA reset (Admin role + 사유 + 감사로그 + 전체 세션 폐기)
- AuditLogs 저장
- 인증 endpoint IP별 10회/분 Rate Limiting
- Swagger Bearer 인증

### 운영 전 반드시 교체
`DevelopmentEmailSender`는 실제 메일을 보내지 않습니다. SMTP/사내메일/메일 공급자 구현체로 교체해야 합니다.
운영 JWT Key는 appsettings.json이 아니라 Secret Manager/환경변수/Key Vault 등으로 주입하십시오.

### DB migration
```
dotnet ef migrations add SecurityV2
dotnet ef database update
```

### 추가 API
- POST `/api/auth/token/refresh`
- POST `/api/auth/token/revoke`
- POST `/api/auth/forgot-password`
- POST `/api/auth/reset-password`
- POST `/api/auth/confirm-email`
- POST `/api/auth/resend-confirmation`
- POST `/api/admin/users/2fa/reset`


## v3 고정 배포/테스트
- SQL 신규 설치: `MigrationsSql/001_IdentityTotp_v4.sql`
- 롤백: `MigrationsSql/001_IdentityTotp_v4_rollback.sql` (전체 인증 테이블 삭제이므로 신규 설치 검증용)
- 테스트: 솔루션 루트에서 `dotnet test IdentityTotpSelfServiceApi.sln`
- 테스트 목록: `TEST_CASES.md`
- VB6: `VB6Sample/modIdentityApi.bas`, `VB6Sample/frmLogin_sample.txt`

주의: 운영 DB에 rollback 스크립트를 실행하면 Identity/토큰/감사 데이터가 삭제됩니다. 운영에서는 백업 및 변경승인 없이 실행하지 마십시오.

## v4 CI / 회귀 테스트

- GitHub Actions: `.github/workflows/ci.yml`
- Windows 로컬 실행: `scripts/run-regression.ps1`
- 회귀 운영 규칙: `REGRESSION_WORKFLOW.md`
- SQL Server 실제 통합 테스트를 사용하려면 `TEST_SQLSERVER_CONNECTION` 환경변수를 설정합니다.
- CI는 SQL Server 2022에 고정 migration을 적용한 뒤 전체 xUnit 회귀 테스트를 실행하고 TRX를 보존합니다.
