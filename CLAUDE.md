# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 폴더 구성

저장소 루트(`D:\work\totp\IdentityTotpSelfServiceApi_v4`)가 솔루션 루트이며, 모든 명령은 여기서 실행한다. 첫 커밋은 인수받은 원본 v4(`..\IdentityTotpSelfServiceApi_v4.zip`)이므로 `git diff <첫 커밋>`으로 인수 이후 변경을 볼 수 있다.

- `IdentityTotpSelfServiceApi_Handover.md`: 이관 및 설계 문서. 요구사항, 회귀 결함 이력(REG-001~011, 최초 빌드 결함 표), 검증 상태(32장), 인수 기준(37장)의 기준 문서다.
- `IdentityTotpSelfServiceApi_Handover2.TXT`: v2→v3→v4 개발 대화 이력(각 버전에서 추가한 기능과 수정한 결함). **CP949** 텍스트다.
- `IdentityTotpSelfServiceApi/`: ASP.NET Core (net10.0) Web API
- `IdentityTotpSelfServiceApi.Tests/`: xUnit v3 통합 테스트 (`WebApplicationFactory<Program>`)
- `tools/WebTestClient/`, `tools/Vb6TestClient/`, `tools/Vb6SampleCheck/`: 실제 서버에 대한 E2E 테스트 클라이언트 (`tools/README.md`)
- `scripts/run-e2e.ps1`: publish → DB 적용 → 서버 기동 → Web(Node·Chrome)/VB6/VB6 샘플 모듈 검증 → 감사 로그 점검
- `REGRESSION_WORKFLOW.md`, `IdentityTotpSelfServiceApi/TEST_CASES.md`: 회귀 테스트 규칙과 테스트 케이스 목록

## 로컬 환경 (이 PC)

- .NET 10 SDK는 시스템이 아니라 `D:\work\totp\.dotnet\dotnet.exe`에 설치되어 있다(PATH에 없음). 시스템 dotnet에는 SDK 8/9만 있다.
- SQL Server: `localhost\SQLEXPRESS`(2022)의 `IdentityTotp_Test`가 테스트 DB다. 두 인스턴스(`localhost`, `localhost\SQLEXPRESS`)에는 다른 프로젝트의 DB가 있으므로 건드리지 않는다. `sqlcmd`는 `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE`(PATH에 없음).
- VB6: `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE` (`/make <vbp> /out <log>`로 빌드)
- Node 22, Chrome 설치됨. `powershell`은 PATH에 없으므로 `$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe`로 호출한다.

## 명령어

```powershell
$d="D:\work\totp\.dotnet\dotnet.exe"
& $d build IdentityTotpSelfServiceApi.sln -c Release
& $d test  IdentityTotpSelfServiceApi.sln -c Release --no-build                       # InMemory
$env:TEST_SQLSERVER_CONNECTION="Server=localhost\SQLEXPRESS;Database=IdentityTotp_Test;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
& $d test  IdentityTotpSelfServiceApi.sln -c Release --no-build                       # SQL Server
& $d test  IdentityTotpSelfServiceApi.sln --filter "FullyQualifiedName~RegressionTests.Refresh_SameTokenConcurrent_AtMostOneSucceeds"
.\scripts\run-regression.ps1 [-ConnectionString "<sql conn>"]   # restore+build+test, TestResults\regression.trx 생성
.\scripts\run-e2e.ps1                                           # 실제 서버 E2E (Web Node/Chrome + VB6). 종료 코드 0이면 통과
.\scripts\run-e2e.ps1 -ServeOnly                                # 시나리오 없이 API(5080)·Web(5090)만 띄움. JWT 키 재사용(.e2e\jwt-dev.key)
```

E2E 서버가 떠 있으면 `bin\Release` DLL이 잠겨 빌드가 실패하므로, E2E는 `.e2e\api`(publish 폴더)에서 실행한다.

DB 스키마는 `IdentityTotpSelfServiceApi/MigrationsSql/NNN_*.sql` 고정 SQL 스크립트가 기준이다. CI와 같은 방식으로 번호 순서대로 `sqlcmd -b ... -i <script>`로 적용한다(`_rollback.sql` 제외). 필터 인덱스 때문에 각 스크립트는 `SET QUOTED_IDENTIFIER ON`으로 시작해야 하고, `sqlcmd -Q`로 Identity 테이블에 직접 쓸 때는 `-I`를 준다. 각 스크립트는 `__EFMigrationsHistory`로 적용 여부를 확인해 여러 번 실행해도 안전해야(idempotent) 한다. 이미 배포된 스크립트는 고치지 말고 다음 번호 스크립트와 rollback 쌍을 추가한다. `001_IdentityTotp_v4_rollback.sql`은 인증 테이블을 전부 삭제하므로 실제 DB에서는 절대 실행하지 않는다. `Data/ApplicationDbContext.cs`의 EF 모델을 변경하면 컬럼 길이와 `Version` 동시성 컬럼을 SQL 스크립트와 일치시킨다.

## 테스트 구조

- `ApiFactory`는 호스트를 `Testing` 환경으로 실행한다. 이 환경에서는 `Program.cs`가 자체 `AddDbContext`를 건너뛰고, 대신 factory가 DB를 등록한다.
  - `TEST_SQLSERVER_CONNECTION` 환경변수가 있으면 실제 SQL Server를 사용한다. CI가 이 방식이며, 동시성 테스트가 의미를 가지려면 이 방식이 필요하다.
  - 없으면 EF InMemory DB를 사용한다. DB 이름은 factory당 한 번만 만든다. `AddDbContext` 람다 안에서 만들면 요청 scope마다 다른 DB가 된다.
- `Program.cs`에서 설정값은 호스트 빌드 전에 읽지 말고 옵션 구성 람다 안에서 읽는다. 그래야 테스트의 설정 덮어쓰기가 반영된다(`Jwt:Key` 결함 사례).
- factory는 다음 설정도 바꾼다.
  - JWT Key, Issuer, Audience를 테스트 값으로 교체한다.
  - 인증 Rate Limit을 1000으로 설정한다. 429 테스트용 `LowRateLimitApiFactory`만 2로 설정한다.
  - `IAppEmailSender`를 `TestEmailSender`로 교체한다. 발송된 메일은 `f.Mail.Messages`에 쌓인다.
  - 만료 토큰 정리 작업을 끈다(`RefreshTokens:Cleanup:Enabled=false`). 정리 로직은 `PurgeExpiredAsync`/`RunOnceAsync`를 직접 호출해 검증한다.
  - 하위 factory는 `ExtraConfig`로 설정을 더하고 `UseTestMail=false`로 실제 발송기를 쓴다. `SmtpApiFactory`는 테스트 SMTP 서버(`FakeSmtpServer`, 127.0.0.1 평문)에 연결하고, `DownSmtpApiFactory`는 닫힌 포트로 SMTP 장애를 만든다. Windows에서는 닫힌 loopback 포트 연결 거부에 약 2초가 걸리므로 SMTP 실패 대기 시간을 넉넉히 둔다.
- 새 테스트(관리자·세션·메일)는 `TestUsers`(`Create`, `Login`, `Enable2Fa`, `Login2Fa`, `Req`, `Audits`)를 쓴다. 관리자 API 테스트는 관리자도 `Enable2Fa` → `Login2Fa`로 MFA 세션을 만들어야 한다.
- `TotpHelper.Generate(sharedKey)`는 Identity와 호환되는 TOTP 코드를 생성하므로, 테스트에 Authenticator 앱이 필요 없다.
- 경쟁 조건은 `BeforeSaveHook`(모든 factory의 DbContext에 등록된 SaveChanges 직전 훅)으로 확정적으로 재현한다(`RevokeRaceTests`). 훅은 static이고 다른 테스트 클래스와 병렬로 돌므로, 특정 사용자 조건으로 거르고 `finally`에서 `null`로 되돌린다.
- Refresh Token을 폐기하는 새 코드는 `RefreshTokenService.RevokeActiveAsync`를 거친다. 충돌 시 재시도하지 않고 직접 `SaveChanges`하면 회전과 겹칠 때 폐기가 통째로 실패한다(REG-013).
- InMemory DB는 문자열 길이를 검사하지 않는다. 컬럼 길이와 관련된 결함(REG-012)은 SQL Server 모드에서만 드러나므로, 길이 제한이 있는 값을 저장하는 코드를 바꾸면 SQL Server로도 테스트한다.
- fixture는 테스트 클래스 단위로 공유되고, CI는 공유 DB에서 실행된다. 따라서 테스트마다 고유 이메일(`$"x-{Guid.NewGuid():N}@test.local"`)을 사용하고, 고정 이메일을 하드코딩하지 않는다. `CreateUser()`가 반환한 사용자의 `Email`을 쓴다.

## 아키텍처

인증 흐름은 `Program.cs`, `Controllers/`, `Services/`에 걸쳐 있다.

- **Identity.** `AddIdentityCore`와 `AddDefaultTokenProviders`를 사용한다. TOTP 검증은 항상 `UserManager.VerifyTwoFactorTokenAsync(..., TokenOptions.DefaultAuthenticatorProvider, code)`로 한다. 서버에서 TOTP를 직접 구현하지 않는다. 주요 설정은 다음과 같다.
  - 이메일 확인 필수
  - 5회 실패 시 잠금(login API가 423 반환)
  - 비밀번호 10자 이상, 복잡도 규칙 적용
- **2단계 로그인.** 2FA 사용자가 `POST /api/auth/login`을 호출하면 Access Token 대신 `requiresTwoFactor=true`와 `challengeToken`을 받는다. 이 토큰은 `purpose=2fa`, `security_stamp`를 담은 수명이 짧은 JWT로, `JwtTokenService.CreateTwoFactorChallenge`가 만든다. `/api/auth/2fa`와 `/api/auth/2fa/recovery`에서 `TwoFactorChallengeService.ValidateAsync`로 검증에 성공해야 실제 토큰이 발급된다. challenge token은 별도 Audience(`Jwt:TwoFactorAudience`, 기본값 `<Audience>/2fa`)로 발급되고, `OnTokenValidated`는 `purpose` 클레임이 있는 토큰을 거부한다. 이 둘 중 하나라도 빠지면 비밀번호만으로 TOTP를 우회할 수 있다(REG-008).
- **2FA setup.** `/2fa/setup`은 SecurityStamp를 바꾸지 않도록 `IUserAuthenticatorKeyStore`에 키만 저장한다. `ResetAuthenticatorKeyAsync`는 stamp를 바꿔 바로 다음 `/enable`이 401이 된다(REG-009). `GET /2fa/qr?format=png|bmp|svg`는 setup한 `otpauth://` URI의 QR 이미지(QRCoder, System.Drawing 미사용)를 돌려준다. QR에는 TOTP Secret이 들어 있으므로 2FA가 꺼진 상태에서만 주고 `no-store`로 응답한다. VB6 화면은 bmp를 임시 파일로 받아 `LoadPicture` 후 바로 지운다.
- **Access JWT 무효화.** 모든 Access Token에는 `security_stamp` 클레임이 들어 있다. `Program.cs`의 `JwtBearerEvents.OnTokenValidated`는 매 요청마다 사용자를 조회해 stamp가 다르면 토큰을 거부한다. 따라서 비밀번호 변경·재설정, 2FA 초기화, 관리자 초기화로 SecurityStamp가 바뀌면 기존 Access Token이 무효화된다. `MapInboundClaims=false`이므로 클레임은 원래 이름을 유지한다. `amr` 클레임 값은 `mfa` 또는 `pwd`다.
- **세션(`sid`).** Access Token의 `sid` 클레임은 함께 발급된 Refresh Token의 `FamilyId`다. 그래서 토큰 발급 순서는 Refresh Token 먼저, 그다음 `JwtTokenService.CreateAccessTokenAsync(user, familyId)`다(`AuthController.IssueTokensAsync`, `TokenController.Refresh`). `OnTokenValidated`는 `RefreshTokenService.IsSessionActiveAsync`로 그 family에 활성 토큰이 있는지도 확인한다. 로그아웃·세션 폐기·재사용 탐지로 family가 끝나면 그 세션의 Access Token만 즉시 거부된다. 세션 API는 `SessionsController`(`/api/account/sessions`)다. `DeviceName`/`UserAgent`는 로그인 때 저장하고 회전할 때 이어받는다. `RefreshTokenCleanupService`가 만료 후 보존 기간이 지난 토큰을 지운다(만료 시각 기준. 회전된 토큰을 일찍 지우면 재사용 탐지가 깨진다).
- **Refresh Token** (`RefreshTokenService`, `Entities/RefreshToken`)
  - DB에는 SHA-256 hex 해시만 저장한다.
  - Refresh할 때마다 같은 `FamilyId` 안에서 새 토큰으로 교체한다(Rotation).
  - `RevokeReason="rotated"`(회전으로 대체된) 토큰이 다시 들어오면 재사용 공격으로 보고 family 전체를 폐기하고 `refresh.reuse`를 기록한다. 로그아웃 등 다른 사유로 폐기된 토큰은 `refresh.revoked`(Detail=사유)로만 기록한다(REG-010). 새 폐기 경로를 추가할 때 `RevokeReason`에 `rotated`를 쓰지 않는다.
  - `Version` 컬럼은 EF 동시성 토큰이다. 같은 토큰을 동시에 회전하면 `DbUpdateConcurrencyException`이 발생하고, 이 경우에도 family 전체를 폐기한다(REG-003).
  - 보안상 중요한 변경을 할 때는 stamp 갱신과 함께 `RevokeAllAsync(userId, ...)`도 호출해야 한다(REG-004: 비밀번호 변경, REG-005: 2FA disable/reset, REG-007: 2FA enable, 전체 기기 로그아웃).
- **2차 인증 잠금.** `/api/auth/2fa`와 `/api/auth/2fa/recovery`는 Identity lockout 카운트를 로그인과 함께 쓴다. 실패하면 `AccessFailedAsync`를 호출하고, 잠금 상태면 423을 반환한다(REG-006).
- **경로.** 토큰 API는 `/api/auth/token/refresh`와 `/api/auth/token/revoke`(로그아웃)다. API 경로를 바꾸면 이관 문서 5장, README, `VB6Sample/modIdentityApi.bas`도 함께 고친다. 인증, 비밀번호, 토큰 컨트롤러에는 `[EnableRateLimiting("auth")]`이 붙어 있다(IP별 고정 윈도우, `RateLimiting:*` 설정).
- **관리자 API.** `AdminController`(`/api/admin/**`)는 `[Authorize(Policy=AdminPolicy.Name)]`이다(`Services/AdminAuthorization.cs`). `AdminMfaHandler`가 요청마다 Admin 역할을 **DB에서** 확인하고(토큰의 role 클레임은 회수 후에도 남으므로 쓰지 않는다), `amr=mfa`와 2FA 활성 상태를 요구한다. 거부는 `AdminAuthorizationResultHandler`가 `admin.denied`로 기록한다. 변경 작업은 사유가 필수(1~500자)다. 새 관리자 API를 추가하면 같은 정책 아래에 두고 감사 이벤트를 남긴다.
- **감사 로그와 Secret.** `AuditService`가 `AuditLogs`에 기록한다. 이벤트 이름 규칙(`<영역>.<동작>`, 실패는 `.failed`, 잠금 거부는 `.locked`)과 전체 목록은 이관 문서 25장에 있다. 보안 관련 API를 추가하면 감사 이벤트도 함께 추가하고 25장 표를 갱신한다. 토큰, 비밀번호, TOTP Secret, 복구 코드는 절대 로그에 남기지 않는다. `AuditService`는 `ActorUserId`(요청한 인증 사용자)를 자동으로 채우고, UserAgent(512)·Detail(2000)을 컬럼 길이에 맞게 자른다. `appsettings.json`의 `Jwt:Key`는 임시값이다.
- **메일.** `IAppEmailSender`는 요청 시점 설정으로 고른다(`Program.cs`). `Email:Smtp:Host`가 있으면 `EmailQueue`(메모리 큐) → `EmailDispatchService`(백그라운드, 재시도) → `SmtpEmailSender`(MailKit)다. 요청 경로에서 SMTP를 기다리지 않는 것은 의도된 설계다(forgot-password 응답 시간으로 계정 존재가 드러나지 않게, SMTP 장애가 500으로 번지지 않게). 동기 발송으로 바꾸지 않는다. 최종 실패는 `email.failed`로 기록한다. Host가 없으면 Development+PickupDirectory는 파일 저장, 그 밖에는 로그만 남긴다(`DevelopmentEmailSender`, 실제 발송 없음). 옵션 클래스의 배열 속성에 기본값을 초기값으로 두지 않는다. 설정 바인더가 설정값을 기본값 뒤에 덧붙인다(`SmtpOptions.RetryDelaysSeconds` 사례).
- **VB6 클라이언트 샘플.** `VB6Sample/IdentityApiSample.vbp`(`modIdentityApi.bas` + `frmLogin.frm`)는 업무 프로그램용 예제다. 소스는 CP949다. `modIdentityApi.bas`의 JSON 추출은 단순 문자열 검색이므로, 응답 형식(특히 `null` 값, 불리언 뒤 쉼표)을 바꾸면 이 모듈도 확인한다. 2FA 사용자를 2FA로 인식하지 못하던 결함이 실제로 있었다(REG-011). 이 모듈을 바꾸면 `tools/Vb6SampleCheck`(run-e2e에 포함)로 검증한다. `frmLogin.frm`(2단계 로그인 화면)을 바꾸면 `tools/Vb6SampleCheck/LoginFormUiTest.ps1`(화면 자동화, 데스크톱 세션 필요, `powershell -STA`)로도 검증한다. VB6 컨트롤은 UI Automation에서 `Pane`으로만 보이고 Label은 읽을 수 없으므로, 클래스 이름(`ThunderRT6TextBox` 등)으로 찾고 WM_SETTEXT/BM_CLICK으로 조작하며 결과는 창 제목과 감사 로그로 확인한다.
- **개발용 메일.** Development 환경에서 `Email:PickupDirectory`를 설정하면 `PickupDirectoryEmailSender`가 메일을 파일로 저장한다. E2E 클라이언트는 여기서 확인·재설정 토큰을 읽는다.

## E2E 테스트 클라이언트

- 시나리오는 `tools/WebTestClient/public/scenario.js`(브라우저와 Node 공용)와 `tools/Vb6TestClient/modScenario.bas`에 같은 순서로 있다. 한쪽을 바꾸면 다른 쪽도 맞춘다.
- 관리자 API는 MFA 세션만 허용하므로 `run-e2e.ps1`이 관리자 계정에 2FA를 켜고 `email:password:totpKey` 형식으로 시나리오에 넘긴다(`--admin`, VB6 `admin=`). 비밀번호에 `:`를 쓰지 않는다.
- API의 JSON 응답은 한글을 `\uXXXX`로 이스케이프한다. VB6처럼 문자열 검색으로 응답을 확인할 때는 한글 값 대신 ASCII 부분으로 찾는다.
- VB6 소스(`.bas`, `.frm`, `.vbp`)는 **CP949 + CRLF**다. Edit/Write 도구는 UTF-8로 쓰므로, 수정한 뒤 PowerShell로 CP949로 다시 저장하거나 처음부터 CP949로 읽고 써서 수정한다. 수정한 다음에는 `VB6.EXE /make`로 빌드되는지 확인한다.
  - 안전한 순서: CP949로 읽어 scratch에 UTF-8 작업본 저장 → 수정 → 줄바꿈을 CRLF로 맞추고 `GetEncoding(949, ExceptionFallback, ...)`로 저장(표현 불가 문자는 예외로 드러남) → `git diff --stat`으로 변경 줄 수가 의도와 같은지 확인.
  - Edit 도구는 작업본 줄바꿈을 LF로 바꿀 수 있다. `perl -CSD -i`는 이 파일을 이중 인코딩하므로 쓰지 않는다(바이트 모드 `perl -0pi`는 괜찮다).
- `scripts/run-e2e.ps1`은 PowerShell 5.1용이라 **UTF-8 BOM**이 있어야 한글이 깨지지 않는다. 출력이 파이프로 넘어가면(Git Bash 등) 스스로 UTF-8로 출력한다.
- `-ServeOnly`/`-KeepRunning`으로 띄운 서버는 호출한 쪽의 출력 파이프를 물려받는다. 출력을 파이프(`| tail`, `*> $null`로 감싼 중첩 호출 등)로 받으면 서버가 살아 있는 동안 명령이 끝나지 않으므로, 파일로 리디렉션하거나 백그라운드로 실행한다.

## 작업 규칙 (이관 문서 및 REGRESSION_WORKFLOW.md 기준)

- 결함을 발견하면 먼저 재현 테스트를 추가하고 실패하는지 확인한다. 그다음 최소 범위로 수정하고, 해당 테스트를 다시 실행한 뒤 전체 테스트를 실행한다. 회귀 테스트는 삭제하지 않는다.
- 테스트를 통과시키려고 Assert를 완화하지 않는다. 원인이 요구사항, 테스트, 코드, 스키마, 시간 의존성 중 어디에 있는지 판단한다. TOTP 테스트는 시간 윈도우에 민감하다.
- 전체 테스트가 통과하기 전에는 Migration이나 API 계약 변경을 배포하지 않는다. 인증 흐름이나 API 계약을 바꿨다면 `run-e2e.ps1`까지 통과시킨다.

## 코드 스타일

상당수 코드가 primary constructor를 쓰고 한 줄에 여러 문장을 몰아 쓰는 압축된 C# 스타일이다. 특히 `Program.cs`, 테스트, `AdminController`, `TokenController`, `PasswordController`가 그렇다. `AuthController`, `TwoFactorController`, 서비스 클래스들은 일반적인 형식을 쓴다. 수정할 때는 해당 파일의 스타일을 따른다. 주석은 한국어로 작성한다.
