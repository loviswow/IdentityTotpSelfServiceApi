# Identity TOTP Self-Service REST API

ASP.NET Core Identity의 내장 Authenticator Token Provider를 사용하는 TOTP 2FA 인증 서버입니다. 회원가입, 이메일 확인, ID/PW 로그인, TOTP 2차 인증, 복구 코드, JWT Access Token, Refresh Token Rotation, 비밀번호 변경·재설정, 관리자 2FA 초기화, 로그인 기기(세션) 관리, SMTP 메일 발송을 REST API로 제공합니다.

- 런타임: .NET 10 (`net10.0`)
- DB: SQL Server
- 테스트: xUnit v3 통합 테스트 (`../IdentityTotpSelfServiceApi.Tests`)

> **검증 상태 (2026-10-02, 로컬):** .NET 10 SDK와 SQL Server 2022 Express에서 다음을 확인했습니다.
> - Release 빌드
> - xUnit 63/63 (InMemory와 SQL Server)
> - E2E: Web 53/53(Node·Chrome), VB6 141/141, VB6 샘플 12/12
>
> SMTP는 테스트 SMTP 서버로 검증했고, 운영 SMTP 서버로 실제 수신은 확인하지 않았습니다. 실제 Authenticator 기기와 운영 HTTPS/프록시도 아직 검증하지 않았습니다. 이관 문서 32장을 참고하십시오.

## 준비

1. **DB 생성 및 스키마 적용.** EF Migration 대신 고정 SQL 스크립트를 사용합니다. `-b` 옵션을 붙여야 SQL 오류가 발생했을 때 실패로 처리됩니다.
   ```powershell
   sqlcmd -S localhost -Q "IF DB_ID('IdentityTotpDb') IS NULL CREATE DATABASE IdentityTotpDb"
   sqlcmd -S localhost -d IdentityTotpDb -b -i MigrationsSql\001_IdentityTotp_v4.sql
   sqlcmd -S localhost -d IdentityTotpDb -b -i MigrationsSql\002_AuditLogs_UserId_Index.sql
   sqlcmd -S localhost -d IdentityTotpDb -b -i MigrationsSql\003_Sessions_AdminAudit.sql
   ```
2. **연결 문자열 설정.** `appsettings.json`의 `ConnectionStrings:DefaultConnection`을 환경에 맞게 수정합니다.
3. **JWT Key 설정.** `Jwt:Key`를 32바이트 이상의 강력한 랜덤 값으로 설정합니다. 저장소의 값은 임시값입니다. 운영 키는 저장소에 커밋하지 말고 환경변수(`Jwt__Key`), Secret Manager, Key Vault 등으로 주입하십시오.
4. **실행.**
   ```powershell
   dotnet run
   ```
   Swagger UI는 `Development` 환경에서만 열립니다.

### 주요 설정 (`appsettings.json`)

| 키 | 기본값 | 설명 |
|---|---|---|
| `Jwt:AccessTokenMinutes` | 15 | Access Token 수명 |
| `Jwt:TwoFactorChallengeMinutes` | 5 | 2FA Challenge Token 수명 |
| `Jwt:RefreshTokenDays` | 14 | Refresh Token 수명 |
| `Totp:Issuer` | `MyCompany SelfService` | Authenticator 앱에 표시되는 발급자 이름 |
| `Jwt:TwoFactorAudience` | `<Jwt:Audience>/2fa` | 2FA Challenge Token 전용 Audience. Access Token과 반드시 달라야 합니다(REG-008). |
| `Email:PickupDirectory` | (없음) | **Development 환경 전용.** 메일을 이 폴더에 파일로 저장합니다(E2E 테스트용). |
| `RateLimiting:AuthPermitLimit` / `AuthWindowSeconds` | 10 / 60 | 인증 API의 IP별 고정 윈도우 Rate Limit |
| `Email:Smtp:Host` | (없음) | 설정하면 SMTP로 메일을 보냅니다(아래 "메일 발송"). 없으면 운영 환경에서는 메일이 발송되지 않습니다. |
| `RefreshTokens:Cleanup:Enabled` / `IntervalMinutes` / `RetentionDays` / `BatchSize` | true / 60 / 30 / 500 | 만료 Refresh Token 정리 작업. 세션의 모든 토큰이 만료 후 `RetentionDays`를 넘긴 경우에만 지웁니다(살아 있는 세션의 회전 토큰은 재사용 탐지에 필요). |

Identity 정책:
- 이메일 확인 필수
- 비밀번호 10자 이상, 대문자·소문자·숫자·특수문자 포함
- 로그인 5회 실패 시 15분 잠금

## 인증 흐름

### 1. 회원가입과 이메일 확인

`POST /api/auth/register`
```json
{ "email": "user@example.com", "password": "Strong!Pass123" }
```
가입에 성공하면 201을 반환하고 확인 메일을 보냅니다. 메일에는 `userId`와 Base64Url로 인코딩된 토큰이 들어 있습니다. 이메일을 확인하기 전에는 로그인할 수 없습니다.

`POST /api/auth/confirm-email`
```json
{ "userId": "...", "token": "..." }
```
확인 메일을 다시 보내려면 `POST /api/auth/resend-confirmation`에 `{ "email": "..." }`를 보냅니다. 응답은 항상 202입니다.

### 2. 로그인

`POST /api/auth/login`
```json
{ "email": "user@example.com", "password": "Strong!Pass123", "deviceName": "영업관리 PC-01" }
```
`deviceName`은 선택 항목입니다. 로그인 기기 목록(`/api/account/sessions`)에 표시되며 128자까지 저장합니다. 2FA 사용자는 세션이 2차 인증 시점에 만들어지므로 `/api/auth/2fa` 또는 `/api/auth/2fa/recovery`에 같은 값을 보냅니다.

2FA를 사용하지 않는 사용자는 바로 토큰을 받습니다.
```json
{ "requiresTwoFactor": false, "accessToken": "...", "expiresAt": "...", "refreshToken": "...", "refreshTokenExpiresAt": "..." }
```

2FA를 사용하는 사용자는 Access Token 대신 수명이 짧은 Challenge Token을 받습니다.
```json
{ "requiresTwoFactor": true, "accessToken": null, "expiresAt": null, "challengeToken": "..." }
```

오류 응답: 비밀번호 오류, 존재하지 않는 사용자, 이메일 미확인은 모두 401, 계정 잠금은 423입니다.

### 3. TOTP 2차 인증

`POST /api/auth/2fa`
```json
{ "challengeToken": "...", "code": "123456" }
```
인증에 성공하면 로그인 응답과 같은 형식으로 Access/Refresh Token을 반환합니다.

TOTP나 복구 코드가 틀리면 로그인 실패와 같은 실패 횟수에 더해집니다. 5회를 넘으면 계정이 잠기고, 이후에는 올바른 코드를 보내도 423을 반환합니다.

Authenticator 기기를 쓸 수 없으면 복구 코드로 로그인합니다. 복구 코드는 한 번만 쓸 수 있습니다.

`POST /api/auth/2fa/recovery`
```json
{ "challengeToken": "...", "recoveryCode": "xxxxx-xxxxx" }
```

### 4. 토큰 갱신과 로그아웃

`POST /api/auth/token/refresh`
```json
{ "refreshToken": "..." }
```
응답 필드는 `accessToken`, `accessTokenExpiresAt`, `refreshToken`, `refreshTokenExpiresAt`입니다. Access Token 만료 시각의 필드명이 로그인 응답의 `expiresAt`과 다르니 주의하십시오.

- Refresh할 때마다 새 Refresh Token이 발급됩니다(Rotation). 이후에는 새 토큰만 사용해야 합니다.
- 이미 사용한 Refresh Token을 다시 보내면 탈취로 간주해 같은 계열(Family)의 토큰을 모두 폐기합니다.
- 같은 토큰으로 동시에 요청하면 최대 하나만 성공합니다.

로그아웃은 `POST /api/auth/token/revoke`에 `{ "refreshToken": "..." }`를 보냅니다. 응답은 204입니다. 로그아웃한 세션의 Access Token도 만료를 기다리지 않고 바로 401이 됩니다.

### 5. 로그인 기기(세션) 관리

로그인할 때마다 세션이 하나 생깁니다. 세션은 Refresh Token 계열(Family)이고, Refresh로 토큰이 바뀌어도 같은 세션입니다. Access Token의 `sid` 클레임이 세션 ID입니다. 세션이 폐기되면 그 세션의 Access Token은 만료 전이라도 거부됩니다.

| Method | 경로 | 응답 | 설명 |
|---|---|---|---|
| GET | `/api/account/sessions` | 200 | 활성 세션 목록. `sessionId`, `deviceName`, `userAgent`, `ipAddress`(마지막 갱신 IP), `createdAt`(로그인 시각), `lastActiveAt`(마지막 갱신 시각), `expiresAt`, `current`(지금 요청한 세션 여부) |
| DELETE | `/api/account/sessions/{sessionId}` | 204 / 404 | 특정 기기 로그아웃. 다른 사용자의 세션이거나 이미 끝난 세션이면 404 |
| POST | `/api/account/sessions/revoke-others` | 200 `{ "revokedSessions": n }` | 지금 쓰는 기기만 남기고 모두 로그아웃 |
| POST | `/api/account/sessions/revoke-all` | 204 | 지금 쓰는 기기를 포함해 전체 로그아웃. SecurityStamp도 바뀝니다. |

응답에는 토큰 원문이나 해시가 들어 있지 않습니다.

## Self-Service API

모든 요청에 `Authorization: Bearer <accessToken>`이 필요합니다.

| Method | 경로 | 요청 본문 | 설명 |
|---|---|---|---|
| GET | `/api/account/me` | - | 내 정보 |
| POST | `/api/account/change-password` | `currentPassword`, `newPassword` | 비밀번호 변경. 기존 Access/Refresh Token 모두 무효화 |
| GET | `/api/account/2fa/status` | - | 2FA 사용 여부, 남은 복구 코드 수 |
| POST | `/api/account/2fa/setup` | - | `sharedKey`, `authenticatorUri` 반환 |
| GET | `/api/account/2fa/qr?format=png\|bmp\|svg` | - | setup한 `authenticatorUri`의 QR 이미지(기본 png, VB6 `LoadPicture`용 bmp). setup 전 400, 2FA 활성 후 409. `Cache-Control: no-store` |
| POST | `/api/account/2fa/enable` | `code` | TOTP 확인 후 2FA 활성화, 복구 코드 10개 반환. 기존 Access/Refresh Token 모두 무효화 |
| POST | `/api/account/2fa/disable` | `password`, `code` | 2FA 비활성화. 기존 Access/Refresh Token 모두 무효화 |
| POST | `/api/account/2fa/reset` | `password`, `code` | Authenticator 기기 변경. 기존 Access/Refresh Token 모두 무효화. 이후 setup → enable 순서로 다시 등록 |
| POST | `/api/account/2fa/recovery-codes/regenerate` | `code` | 복구 코드 재발급. 기존 코드는 무효화 |

### Authenticator 등록 순서

1. `POST /api/account/2fa/setup`을 호출합니다. 클라이언트는 `authenticatorUri`를 QR 코드로 보여 주거나 `sharedKey`를 직접 입력하게 합니다. QR을 직접 그리기 어려우면 `GET /api/account/2fa/qr`로 이미지를 받아 표시합니다(VB6는 `?format=bmp`를 임시 파일로 받아 `LoadPicture`, 표시 후 파일 삭제). QR에는 TOTP Secret이 들어 있으므로 등록이 끝나면 화면에서 지웁니다.
2. 앱에 표시된 6자리 코드로 `POST /api/account/2fa/enable`을 호출합니다.
   ```json
   { "code": "123456" }
   ```
3. 응답의 `recoveryCodes`는 이때 **한 번만** 표시됩니다. 사용자가 안전한 곳에 보관하도록 안내하십시오.
4. 활성화하면 기존 Access/Refresh Token이 모두 무효화됩니다. 클라이언트는 복구 코드를 보여 준 뒤 다시 로그인해서 TOTP 2차 인증을 거쳐야 합니다.

## 비밀번호 분실

| Method | 경로 | 요청 본문 | 응답 |
|---|---|---|---|
| POST | `/api/auth/forgot-password` | `email` | 계정 존재 여부와 관계없이 202 |
| POST | `/api/auth/reset-password` | `email`, `token`, `newPassword` | 204. 기존 Access/Refresh Token 모두 무효화 |

재설정 토큰은 메일로 전달되며 Base64Url로 인코딩되어 있습니다.

## 관리자 API

관리자 API는 다음 조건을 **모두** 만족하는 요청만 허용합니다(`AdminMfa` 정책). 하나라도 어긋나면 403이며 본문의 `reason`에 `not-admin` 또는 `mfa-required`가 담깁니다. 거부도 감사 로그(`admin.denied`)에 남습니다.
- `Admin` 역할이 있습니다. 역할은 토큰이 아니라 **요청마다 DB에서** 확인하므로, 역할을 회수하면 이미 발급된 토큰도 바로 거부됩니다.
- TOTP나 복구 코드로 2차 인증을 거쳐 로그인한 세션입니다(`amr=mfa`). 비밀번호만으로 로그인한 관리자는 쓸 수 없습니다.
- 지금도 2FA가 켜져 있습니다.

| Method | 경로 | 요청 | 설명 |
|---|---|---|---|
| POST | `/api/admin/users/2fa/reset` | `userId`, `reason` | 대상 사용자의 2FA를 끄고 Authenticator 키를 교체합니다. 기존 Access/Refresh Token은 모두 무효화됩니다. 자기 계정은 400(self-service API를 사용) |
| GET | `/api/admin/users/{userId}/sessions` | - | 대상 사용자의 활성 세션 목록 |
| POST | `/api/admin/users/sessions/revoke-all` | `userId`, `reason` | 대상 사용자 전체 기기 로그아웃(침해 대응). SecurityStamp도 바뀝니다. |
| GET | `/api/admin/audit-logs` | `userId`, `eventType`, `from`, `to`, `limit`(1~500, 기본 100) | 감사 로그 조회(최신순). 조회도 감사 로그(`admin.audit.read`)에 남습니다. |

- `reason`은 필수이고 앞뒤 공백을 뺀 500자까지입니다. 없거나 길면 400입니다.
- 감사 로그의 `userId`는 대상 사용자, `actorUserId`는 작업을 한 관리자입니다.
- 역할을 부여하는 API는 없습니다. `AspNetRoles`, `AspNetUserRoles` 테이블에 직접 등록하십시오. 관리자 계정은 2FA를 먼저 켜야 관리자 API를 쓸 수 있습니다.

## 메일 발송 (SMTP)

`Email:Smtp:Host`를 설정하면 MailKit으로 SMTP 발송합니다. 설정이 없으면 Development 환경에서는 `Email:PickupDirectory`(파일 저장) 또는 로그 출력만 하고, 그 밖의 환경에서는 메일을 보내지 않고 기동 시 경고를 남깁니다.

| 키 | 기본값 | 설명 |
|---|---|---|
| `Email:Smtp:Host` / `Port` | - / 587 | SMTP 서버 |
| `Email:Smtp:Security` | `StartTls` | `StartTls`(587), `SslOnConnect`(465), `Auto`, `None`(평문, 사내 릴레이·테스트 전용) |
| `Email:Smtp:UserName` / `Password` | - | SMTP 인증. 비워 두면 인증하지 않습니다. **Password는 저장소에 넣지 말고 환경 변수(`Email__Smtp__Password`)나 Secret Store로 주입합니다.** |
| `Email:Smtp:FromAddress` / `FromName` | - | 보내는 사람. Host를 설정하면 `FromAddress`는 필수이며, 없으면 기동이 실패합니다. |
| `Email:Smtp:TimeoutSeconds` | 30 | 연결·전송 제한 시간 |
| `Email:Smtp:RetryDelaysSeconds` | `[5, 30, 120]` | 일시 장애 시 재시도 간격. 항목 수만큼 재시도합니다. 영구 오류(5xx, 없는 주소 등)는 재시도하지 않습니다. |
| `Email:Smtp:MaxConcurrency` | 4 | 동시에 발송하는 작업자 수. 재시도를 기다리는 메일이 다른 메일을 막지 않게 합니다. |

- API는 메일을 메모리 큐에 넣고 바로 응답합니다. 실제 발송은 백그라운드에서 합니다. 그래서 SMTP 장애가 회원가입·비밀번호 재설정 API의 오류로 번지지 않고, `forgot-password` 응답 시간으로 계정 존재 여부가 드러나지 않습니다.
- 재시도까지 모두 실패하면 오류 로그와 감사 로그(`email.failed`, `Detail`=메일 제목)를 남깁니다. 큐는 메모리에만 있으므로 프로세스가 내려가면 보내지 못한 메일은 사라집니다. 사용자는 `resend-confirmation`, `forgot-password`로 다시 요청할 수 있습니다.
- 서버 인증서 검증은 끄지 않습니다. 메일 본문(확인·재설정 토큰)은 로그에 남기지 않습니다.
- 메일 본문 형식(`Confirmation token: ...; userId: ...`, `Reset token: ...`)은 클라이언트가 링크나 화면을 구성하는 데 쓰는 최소 형식입니다. 사용자용 문구나 링크가 필요하면 `PasswordController`, `AuthController`의 본문을 바꿉니다(E2E 클라이언트가 이 형식을 파싱합니다).

## 보안 동작 요약

- **Access Token 즉시 무효화.** JWT에 `security_stamp` 클레임을 넣고 요청마다 DB 값과 비교합니다. 비밀번호 변경·재설정, 2FA 활성화·비활성화·초기화, 전체 기기 로그아웃 후에는 기존 Access Token이 즉시 거부됩니다. 또 `sid`(세션) 클레임이 가리키는 세션이 로그아웃·세션 폐기·재사용 탐지로 끝났으면 그 Access Token도 거부합니다.
- **만료 토큰 정리.** `RefreshTokenCleanupService`가 주기적으로(기본 60분) 만료 후 보존 기간(기본 30일)이 지난 Refresh Token을 지웁니다.
- **Refresh Token 저장.** DB에는 SHA-256 해시만 저장합니다.
- **Rate Limiting.** `/api/auth/**` 전체에 IP별 Rate Limit이 적용되며, 초과하면 429를 반환합니다.
- **감사 로그.** 가입, 로그인 성공·실패, 계정 잠금, TOTP·복구 코드 인증, 2FA 등록·활성화·해제·초기화, 복구 코드 재발급, Refresh, 재사용 탐지, 로그아웃, 세션 폐기, 비밀번호 변경·재설정, 관리자 작업과 거부, 메일 발송 실패를 `AuditLogs`에 기록합니다. 작업한 사용자는 `ActorUserId`에 남습니다. 토큰, 비밀번호, TOTP Secret, 복구 코드 원문은 기록하지 않습니다. 이벤트 목록은 이관 문서 25장을 참고하십시오.

## DB 스크립트

| 파일 | 용도 |
|---|---|
| `MigrationsSql/001_IdentityTotp_v4.sql` | 신규 설치. 여러 번 실행해도 안전 |
| `MigrationsSql/001_IdentityTotp_v4_rollback.sql` | 인증 테이블 전체 삭제. 신규 설치 검증용 |
| `MigrationsSql/002_AuditLogs_UserId_Index.sql` | 사용자별 감사 로그 조회 인덱스. 001 다음에 적용 |
| `MigrationsSql/002_AuditLogs_UserId_Index_rollback.sql` | 002 인덱스만 제거. 데이터 영향 없음 |
| `MigrationsSql/003_Sessions_AdminAudit.sql` | 세션 표시 컬럼(`RefreshTokens.DeviceName`, `UserAgent`), 만료 정리 인덱스, `AuditLogs.ActorUserId`. 002 다음에 적용 |
| `MigrationsSql/003_Sessions_AdminAudit_rollback.sql` | 003 컬럼·인덱스 제거. 기기 이름과 감사 수행자 값이 삭제됩니다. API를 003 이전 버전으로 되돌린 뒤 실행 |

번호 순서대로(`001` → `002` → …) 적용합니다. rollback 파일은 제외합니다.

> **경고:** 운영 DB에서 rollback 스크립트를 실행하면 사용자, 토큰, 감사 데이터가 모두 삭제됩니다. 백업과 변경 승인 없이 실행하지 마십시오.

EF 모델(`Data/ApplicationDbContext.cs`)을 변경하면 SQL 스크립트의 컬럼 길이와 `RefreshTokens.Version` 동시성 컬럼도 함께 맞춰야 합니다.

## 테스트

솔루션 루트(`IdentityTotpSelfServiceApi_v4/`)에서 실행합니다.

```powershell
dotnet test IdentityTotpSelfServiceApi.sln
dotnet test IdentityTotpSelfServiceApi.sln --filter "FullyQualifiedName~AuthFlowTests"
.\scripts\run-regression.ps1 -ConnectionString "<SQL Server 연결 문자열>"
```

- `TEST_SQLSERVER_CONNECTION` 환경변수를 설정하면 실제 SQL Server에서 테스트하고, 없으면 InMemory DB를 사용합니다. 동시 Refresh 테스트는 실제 SQL Server에서 실행해야 의미가 있습니다. 사용할 DB에는 위의 SQL 스크립트를 먼저 적용하십시오.
- 테스트 목록은 `TEST_CASES.md`, 회귀 테스트 규칙은 `../REGRESSION_WORKFLOW.md`를 참고하십시오.
- 실제 서버에 대한 Web·VB6 E2E 테스트는 `..\scripts\run-e2e.ps1`로 실행합니다. 테스트 클라이언트 설명은 `../tools/README.md`에 있습니다.
- CI(`../.github/workflows/ci.yml`)는 다음 순서로 실행됩니다.
  1. SQL Server 2022 컨테이너에 SQL 스크립트를 적용합니다.
  2. 전체 테스트를 실행합니다.
  3. 결과 TRX 파일을 Artifact로 보존합니다.

## VB6 클라이언트

`VB6Sample/IdentityApiSample.vbp`는 VB6 IDE에서 바로 열 수 있는 로그인 샘플 프로젝트입니다. WinHttp로 로그인 → TOTP → 내 정보 → Refresh → 로그아웃을 호출합니다.
- `modIdentityApi.bas`: 업무 프로그램에 그대로 가져다 쓰는 API 모듈입니다. `ApiInit`, `ApiLogin`, `ApiTotp`, `ApiGet`, `ApiRefresh`, `ApiLogout`, `gLastStatus`를 제공합니다. `ApiLogin`과 `ApiTotp`의 마지막 인자 `deviceName`(선택)을 주면 로그인 기기 목록에 그 이름이 표시됩니다(예: `"영업관리 " & Environ$("COMPUTERNAME")`).
- `frmLogin.frm`: 사용 예입니다. 401이 나면 Refresh를 한 번만 시도하고, 실패하면 다시 로그인하도록 안내합니다. 423(잠금)은 따로 안내합니다.
- API 주소는 화면에서 입력합니다(기본값 `http://localhost:5080`). 운영에서는 HTTPS 주소를 쓰고, 서버 인증서 검증을 끄지 마십시오.
- 소스는 VB6용 **CP949(ANSI)**로 저장되어 있습니다. UTF-8로 저장하면 한글이 깨집니다.

VB6 클라이언트 구현 시 주의사항:
- HTTPS만 사용하고, 서버 인증서 검증을 끄지 않습니다.
- Refresh 후에는 반드시 새 Refresh Token으로 교체하고, Refresh가 실패하면 로그인 화면으로 돌아갑니다.
- 토큰을 로그나 화면에 출력하지 않습니다.

## 운영 전 필수 작업

- `Email:Smtp:*`를 설정하고 실제 메일 수신을 확인합니다. 설정하지 않으면 메일이 발송되지 않습니다.
- JWT Key, DB 비밀번호, SMTP 비밀번호를 외부 Secret Store로 옮깁니다.
- 관리자 계정은 2FA를 켜야 관리자 API를 쓸 수 있습니다. 배포 전에 관리자 2FA 등록을 안내합니다.
- HTTPS와 HSTS를 적용합니다. Reverse Proxy를 사용하면 Forwarded Headers도 설정합니다. 설정하지 않으면 Rate Limit과 감사 로그의 IP가 프록시 IP로 기록됩니다.
- CORS Allow List를 설정합니다.
- 서버의 NTP 시간 동기화를 확인합니다. 서버 시간이 어긋나면 정상 TOTP 코드도 실패합니다.
- 만료 Refresh Token 정리 작업(`RefreshTokens:Cleanup:*`)의 보존 기간을 운영 정책에 맞춥니다.
