# ASP.NET Core Identity TOTP 2FA Self-Service REST API
## 프로젝트 이관 문서

- 기준 버전: **IdentityTotpSelfServiceApi v4**
- 작성 기준일: 2026-10-01
- 대상: ASP.NET Core Identity 기반 인증/2FA REST API 후속 개발자 및 운영 담당자
- 주요 연동 대상: Web/SPA/Mobile/VB6 Client
- DB: SQL Server
- 테스트: xUnit Integration Test
- CI: GitHub Actions + SQL Server 2022

---

# 1. 프로젝트 목적

본 프로젝트는 ASP.NET Core Identity의 내장 기능을 활용하여 TOTP 기반 2단계 인증(2FA)을 REST API 형태로 제공하기 위한 Self-Service 인증 서버이다.

직접 TOTP 알고리즘을 인증 서버 로직에 구현하기보다 ASP.NET Core Identity가 제공하는 Authenticator Token Provider를 사용한다.

주요 목표는 다음과 같다.

1. 회원가입
2. 이메일 확인
3. ID/Password 인증
4. TOTP Authenticator 등록
5. TOTP 2차 인증
6. Recovery Code
7. JWT Access Token
8. Refresh Token Rotation
9. Refresh Token 재사용 공격 탐지
10. Logout 및 Token Revoke
11. 비밀번호 변경/재설정
12. 관리자에 의한 2FA 초기화
13. SecurityStamp 기반 기존 세션 무효화
14. 감사 로그
15. Rate Limiting
16. Swagger API 테스트
17. xUnit 통합/회귀 테스트
18. VB6 Client REST API 연동

---

# 2. 전체 인증 구조

기본 인증 흐름은 다음과 같다.

```text
Client
  |
  | ID / Password
  v
POST /api/auth/login
  |
  +-- 2FA OFF --------------------+
  |                               |
  |                         Access Token
  |                         Refresh Token
  |
  +-- 2FA ON
          |
          v
   2FA Challenge Token
          |
          | Authenticator TOTP
          v
POST /api/auth/2fa
          |
          v
   Identity TOTP Verify
          |
     +----+----+
     |         |
   실패       성공
     |         |
    401        v
          Access Token
          Refresh Token
```

핵심 원칙은 2FA 사용자의 경우 비밀번호만 맞았다고 실제 Access Token을 발급하지 않는 것이다.

비밀번호 인증 후 짧은 수명의 2FA Challenge Token만 발급하고 TOTP 검증이 성공해야 최종 Access/Refresh Token을 발급한다.

---

# 3. ASP.NET Core Identity TOTP

Identity 등록 시 다음 구성을 사용한다.

```csharp
.AddIdentityCore<ApplicationUser>(...)
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();
```

`AddDefaultTokenProviders()`를 통해 Authenticator Token Provider를 등록한다.

TOTP 검증 핵심 코드는 다음 형태이다.

```csharp
var valid = await userManager.VerifyTwoFactorTokenAsync(
    user,
    TokenOptions.DefaultAuthenticatorProvider,
    code);
```

따라서 인증 서버에서 독자적인 TOTP 인증 알고리즘을 작성하지 않는다.

---

# 4. TOTP Self-Service 등록

사용자는 로그인 후 다음 순서로 자신의 Authenticator를 등록한다.

```text
Access Token
     |
     v
POST /api/account/2fa/setup
     |
     +-- SharedKey
     +-- otpauth URI
             |
             v
 Microsoft Authenticator
 Google Authenticator
 기타 TOTP 호환 앱
             |
             v
        6자리 TOTP
             |
             v
POST /api/account/2fa/enable
             |
             v
      TOTP 검증 성공
             |
             v
      2FA Enabled
             |
             v
      Recovery Codes
```

Recovery Code는 사용자에게 한 번 표시하고 안전한 장소에 별도 보관하도록 해야 한다.

---

# 5. 주요 REST API

## 인증

| Method | API | 설명 |
|---|---|---|
| POST | `/api/auth/register` | 회원가입 |
| POST | `/api/auth/login` | ID/PW 로그인 |
| POST | `/api/auth/2fa` | TOTP 2차 인증 |
| POST | `/api/auth/2fa/recovery` | Recovery Code 로그인 |
| POST | `/api/auth/token/refresh` | Refresh Token Rotation |
| POST | `/api/auth/token/revoke` | Logout/Refresh Token 폐기 |

## 사용자 Self-Service

| Method | API | 설명 |
|---|---|---|
| GET | `/api/account/me` | 사용자 정보 |
| POST | `/api/account/change-password` | 비밀번호 변경 |
| GET | `/api/account/2fa/status` | 2FA 상태 |
| POST | `/api/account/2fa/setup` | Authenticator Secret 생성 |
| POST | `/api/account/2fa/enable` | TOTP 확인 후 2FA 활성화 |
| POST | `/api/account/2fa/disable` | 2FA 비활성화 |
| POST | `/api/account/2fa/reset` | Authenticator 재등록 |
| POST | `/api/account/2fa/recovery-codes/regenerate` | Recovery Code 재발급 |
| GET | `/api/account/sessions` | 로그인 기기(세션) 목록 (39장) |
| DELETE | `/api/account/sessions/{sessionId}` | 특정 기기 로그아웃 |
| POST | `/api/account/sessions/revoke-others` | 현재 기기 외 모두 로그아웃 |
| POST | `/api/account/sessions/revoke-all` | 전체 기기 로그아웃 |

로그인(`/login`, `/2fa`, `/2fa/recovery`) 요청에는 선택 항목 `deviceName`(최대 128자)을 보낼 수 있다. 세션 목록에 표시된다.

## 계정 복구

| Method | API | 설명 |
|---|---|---|
| POST | `/api/auth/confirm-email` | 이메일 확인 (`userId`, `token`) |
| POST | `/api/auth/resend-confirmation` | 확인 메일 재발송. 항상 202 |
| POST | `/api/auth/forgot-password` | 비밀번호 재설정 메일 발송. 계정 존재 여부와 관계없이 202 |
| POST | `/api/auth/reset-password` | 비밀번호 재설정 (`email`, `token`, `newPassword`) |

메일로 전달되는 토큰은 Base64Url로 인코딩되어 있다.

운영 환경에서는 `Email:Smtp:*`를 설정해 SMTP로 발송한다(41장).

## 관리자

| Method | API | 설명 |
|---|---|---|
| POST | `/api/admin/users/2fa/reset` | 대상 사용자 2FA 초기화 (`userId`, `reason`) |
| GET | `/api/admin/users/{userId}/sessions` | 대상 사용자 세션 목록 |
| POST | `/api/admin/users/sessions/revoke-all` | 대상 사용자 전체 기기 로그아웃 (`userId`, `reason`) |
| GET | `/api/admin/audit-logs` | 감사 로그 조회 (`userId`, `eventType`, `from`, `to`, `limit` 1~500) |

- Admin 역할(요청마다 DB 확인) + 2차 인증을 거친 세션(`amr=mfa`) + 2FA 활성 상태여야 한다. 하나라도 아니면 403이다(40장).
- `reason`은 필수이며 500자까지다. 없거나 길면 400이다. 자기 계정의 2FA 초기화는 400이다.
- 역할을 부여하는 API는 없다. `AspNetRoles`, `AspNetUserRoles` 테이블에 직접 등록한다. 관리자 계정은 2FA를 먼저 켜야 관리자 API를 쓸 수 있다.

`/api/auth/**` 아래의 모든 API에는 IP별 Rate Limit(`auth` 정책)이 적용된다.

---

# 6. Access Token

Access Token은 JWT를 사용한다.

권장 기본 수명은 짧게 유지한다.

현재 설계 기준 예:

```text
Access Token  : 약 15분
Refresh Token : 약 14일
2FA Challenge : 약 5분
```

운영 정책에 따라 조정할 수 있다.

JWT에는 사용자 식별 정보와 인증 관련 클레임을 포함한다.

2FA 인증 완료 사용자는 인증 강도를 식별할 수 있도록 `amr` 등의 클레임을 사용할 수 있다.

---

# 7. SecurityStamp

SecurityStamp는 중요한 보안 변경 후 기존 인증 상태를 무효화하기 위해 사용한다.

다음 작업에서 SecurityStamp 변경을 고려/적용한다.

- 비밀번호 변경
- 비밀번호 재설정
- 관리자 2FA 초기화
- Authenticator 초기화
- 보안상 강제 로그아웃 (사용자 전체 기기 로그아웃, 관리자 전체 기기 로그아웃)

Access JWT 검증 시 DB의 현재 SecurityStamp와 Token의 값을 비교하는 구조를 적용하여 중요한 보안 변경 후 기존 Access Token도 거부할 수 있도록 설계했다.

세션 단위 무효화도 함께 쓴다. Access Token의 `sid` 클레임은 함께 발급된 Refresh Token family(세션)를 가리킨다. `OnTokenValidated`는 그 세션에 아직 쓸 수 있는 Refresh Token이 있는지 확인하고, 없으면 거부한다. 그래서 로그아웃, 특정 기기 로그아웃, 재사용 탐지로 세션이 끝나면 그 세션의 Access Token만 바로 무효가 된다. 다른 기기는 영향을 받지 않는다(39장).

---

# 8. Refresh Token 설계

Refresh Token은 원문을 DB에 저장하지 않는다.

```text
Client Refresh Token
        |
        v
     SHA-256
        |
        v
DB TokenHash
```

DB가 유출되더라도 저장된 Hash만으로 원래 Refresh Token을 바로 사용할 수 없도록 한다.

---

# 9. Refresh Token Rotation

Refresh 요청이 성공하면 기존 Refresh Token을 다시 사용하지 않는다.

```text
Refresh Token A
      |
      v
POST /api/auth/token/refresh
      |
      +-- A = Used/Replaced
      |
      v
Refresh Token B
```

이후 Client는 B만 사용해야 한다.

---

# 10. Refresh Token 재사용 공격 탐지

이미 사용한 Refresh Token A가 다시 들어오는 경우 정상적인 Client 동작으로 보지 않는다.

```text
A 사용
 |
 +--> B 발급

A 재사용 시도
 |
 v
Reuse Detection
 |
 v
Token Family Revoke
```

탈취된 Refresh Token이 공격자에게 재사용되는 상황을 방어하기 위한 구조이다.

재사용 탐지 대상은 **회전으로 대체된(`RevokeReason=rotated`) 토큰**뿐이다. 로그아웃, 비밀번호 변경, 2FA 변경, 관리자 초기화로 폐기된 토큰은 401로 거부하되 `refresh.revoked`로만 기록한다(REG-010, 25장).

---

# 11. 동시 Refresh 경쟁조건 수정

v3 검토 과정에서 동일 Refresh Token에 두 요청이 거의 동시에 들어오면 두 요청 모두 기존 Token을 Active 상태로 읽을 가능성이 확인되었다.

이 경우 두 개의 신규 Refresh Token이 발급될 가능성이 있다.

v4에서 이를 보완하였다.

핵심 전략:

```text
RefreshTokens.Version
        +
EF Core Concurrency Token
        +
DB Update 충돌 감지
```

동일 Refresh Token에 대한 동시 요청 시 최대 하나의 정상 Rotation만 허용하도록 한다.

Concurrency 충돌이 발생하면 보수적으로 해당 Token Family를 폐기하는 방향으로 처리한다.

---

# 12. 비밀번호 변경 후 Refresh Token 문제 수정

검토 중 다음 문제가 확인되었다.

기존 구현에서는 비밀번호 변경 시 SecurityStamp는 변경되지만 기존 Refresh Token이 살아 있을 수 있었다.

그 상태에서 기존 Refresh Token을 사용하면 새 SecurityStamp를 가진 Access Token을 다시 발급받을 가능성이 있었다.

v4에서는 비밀번호 변경 성공 시:

```text
Change Password
      |
      +--> SecurityStamp 변경
      |
      +--> 사용자 Refresh Token 전체 폐기
```

하도록 수정하였다.

회귀 테스트 조건:

```text
1. 로그인
2. Access/Refresh 확보
3. 비밀번호 변경
4. 기존 Access Token -> 실패
5. 기존 Refresh Token -> 실패
6. 기존 비밀번호 로그인 -> 실패
7. 새 비밀번호 로그인 -> 성공
```

---

# 13. Logout

Logout 시 현재 Refresh Token을 폐기한다.

Logout 이후 동일 Refresh Token으로 Refresh를 시도하면 실패해야 한다.

JWT Access Token은 기본적으로 stateless이므로 단순 Logout만으로 이미 발급된 JWT가 즉시 사라지는 것은 아니다.

본 프로젝트에서는 SecurityStamp 검증 및 Refresh Token 폐기 정책을 함께 사용한다. 2026-10-02부터는 Access Token의 `sid`로 세션 상태도 확인하므로, 로그아웃한 세션의 Access Token은 만료 전이라도 바로 401이 된다(7장, 39장).

---

# 14. Recovery Code

Authenticator 기기를 사용할 수 없는 경우 Recovery Code를 사용할 수 있다.

Recovery Code는 일회용이어야 한다.

테스트 조건:

```text
Recovery Code 최초 사용 -> 성공
동일 Recovery Code 재사용 -> 실패
```

Recovery Code 재생성 시 기존 코드는 더 이상 사용하지 않는 것을 원칙으로 한다.

---

# 15. 계정 잠금

로그인 실패가 반복되는 경우 ASP.NET Core Identity Lockout을 사용한다.

현재 테스트 기준:

```text
잘못된 비밀번호 반복
       |
       v
AccessFailedCount 증가
       |
       v
설정된 횟수 초과
       |
       v
Account Locked
```

Lockout 정책은 운영 정책에 맞게 조정한다.

---

# 16. Rate Limiting

인증 API는 Brute Force 공격 대상이므로 Rate Limiting을 적용한다.

특히 다음 API가 중요하다.

- Login
- TOTP Verify
- Recovery Login
- Password Reset
- Email confirmation resend

테스트에는 429 Too Many Requests 경계조건을 포함한다.

---

# 17. SQL Server Migration

v4에서는 DB 구조를 고정할 수 있도록 SQL Server Migration 스크립트를 포함한다.

주요 대상:

- ASP.NET Core Identity Tables
- RefreshTokens
- AuditLogs
- RefreshTokens.Version concurrency column
- 관련 Index/Constraint

EF 모델의 문자열 길이와 SQL Script의 컬럼 길이를 일치시키도록 수정하였다.

이 작업은 향후 EF Migration을 생성했을 때 불필요한 ALTER COLUMN이 발생하는 문제를 줄이기 위함이다.

---

# 18. DB Migration 운영 원칙

운영 DB에서는 Migration을 즉시 자동 적용하기보다 다음 절차를 권장한다.

```text
개발
 |
 v
Migration SQL 생성
 |
 v
Test DB 적용
 |
 v
Integration Test
 |
 v
Schema 비교
 |
 v
Backup
 |
 v
운영 승인
 |
 v
Production 적용
```

Rollback 스크립트도 함께 관리한다.

---

# 19. xUnit 통합 테스트

단순 Unit Test가 아니라 실제 API 호출 관점의 Integration Test를 중심으로 구성한다.

주요 테스트 영역:

## 회원/로그인

- 정상 회원가입
- 중복 이메일
- 이메일 미확인
- 이메일 확인
- 정상 로그인
- 잘못된 비밀번호
- 존재하지 않는 사용자
- 반복 실패 Lockout

## TOTP

- Setup
- Enable
- 올바른 TOTP
- 잘못된 TOTP
- 이미 활성화된 2FA 재활성화
- Disable
- Reset
- Recovery Code

## Refresh Token

- 정상 Refresh
- Rotation
- 이전 Token 재사용
- Token Family Revoke
- Logout 이후 Refresh
- 만료 Token
- 잘못된 Token
- 동시 Refresh

## SecurityStamp

- 비밀번호 변경 후 기존 Access Token
- 비밀번호 변경 후 기존 Refresh Token
- 2FA Reset 후 기존 Token
- 관리자 Reset 후 기존 인증 상태

## 관리자

- 일반 사용자 호출 -> 403
- 관리자 정상 호출
- 존재하지 않는 사용자
- 2FA Reset

## Rate Limit

- 허용 범위 내 요청
- 제한 경계
- 제한 초과 -> 429

---

# 20. TOTP 통합 테스트

외부 Microsoft Authenticator 앱을 자동화 테스트에서 직접 사용할 필요가 없도록 테스트 코드에서 Identity와 호환되는 TOTP 값을 생성하는 Helper를 둔다.

이를 통해 다음 흐름을 자동 테스트할 수 있다.

```text
Register
  |
Confirm Email
  |
Login
  |
2FA Setup
  |
TOTP 생성
  |
2FA Enable
  |
Logout
  |
Login
  |
TOTP Challenge
  |
TOTP 생성
  |
2FA Login
```

---

# 21. 회귀 테스트 원칙

오류가 발견되면 단순 코드 수정으로 끝내지 않는다.

반드시 다음 절차를 따른다.

```text
장애/결함 발견
      |
      v
재현 테스트 작성
      |
      v
테스트 실패 확인
      |
      v
원인 분석
      |
      v
최소 범위 수정
      |
      v
해당 테스트 재실행
      |
      v
관련 테스트 실행
      |
      v
전체 Regression Test
      |
      v
통과
```

한 번 발견된 결함의 재현 테스트는 삭제하지 않는다.

이 테스트가 향후 동일 결함이 다시 들어오는 것을 방지한다.

---

# 22. 현재까지 발견하여 수정한 주요 회귀 결함

## REG-001 회원가입 이메일 확인 흐름

문제:

`RequireConfirmedEmail=true`인데 회원가입 직후 확인메일 발송 흐름이 빠져 있었다.

결과:

신규 사용자가 별도 resend 동작을 하지 않으면 로그인할 수 있었다.

개선:

회원가입 시 이메일 확인 토큰을 생성하여 Email Sender를 통해 전달하도록 수정.

---

## REG-002 Login Rate Limit

문제:

로그인 Endpoint에 Rate Limit 적용이 누락될 수 있는 구조 확인.

개선:

인증 Endpoint에 Rate Limit 정책 적용.

---

## REG-003 Refresh Token 동시 Rotation

문제:

동일 Refresh Token을 동시에 Refresh할 경우 둘 다 Active 상태를 읽어 복수 신규 Token이 발급될 가능성.

개선:

Concurrency Version 적용 및 충돌 처리.

---

## REG-004 Password Change 이후 Refresh Token

문제:

비밀번호 변경 후 기존 Refresh Token으로 다시 Access Token을 얻을 가능성.

개선:

비밀번호 변경 성공 시 SecurityStamp 갱신 + 사용자 Refresh Token 전체 폐기.

---

## REG-005 2FA Disable/Reset 이후 Refresh Token

문제:

`/api/account/2fa/disable`, `/api/account/2fa/reset`이 SecurityStamp만 갱신하고 Refresh Token은 폐기하지 않았다.

결과:

2FA 해제 또는 Authenticator 초기화 후에도 기존 Refresh Token으로 새 Access Token을 받을 수 있었다. REG-004와 같은 유형의 결함이다.

개선:

disable/reset 성공 시 SecurityStamp 갱신 + 사용자 Refresh Token 전체 폐기.

회귀 테스트: `RegressionTests.TwoFactorDisableOrReset_RevokesExistingRefreshTokens`

---

## REG-006 2차 인증 실패 시 계정 잠금 미적용

문제:

`/api/auth/2fa`는 TOTP 실패 시 실패 횟수만 증가시키고, 계정이 잠겼는지는 확인하지 않았다. `/api/auth/2fa/recovery`는 실패 횟수도 증가시키지 않았다.

결과:

비밀번호를 아는 공격자가 Challenge Token으로 TOTP나 복구 코드를 계속 대입할 수 있었다. 이를 막는 수단은 Rate Limit뿐이었다.

개선:

두 API 모두 Challenge Token 검증 후 계정이 잠겨 있으면 423을 반환한다. 복구 코드 실패도 로그인 실패와 같은 실패 횟수에 포함한다.

회귀 테스트:

- `RegressionTests.TotpFailures_LockAccount_AndCorrectCodeIsRejectedWith423`
- `RegressionTests.RecoveryCodeFailures_LockAccount_AndValidCodeIsRejectedWith423`

---

## REG-007 2FA Enable 이전 Refresh Token

문제:

`/api/account/2fa/enable`이 SecurityStamp만 갱신하고 Refresh Token은 폐기하지 않았다.

결과:

2FA 활성화 전에 비밀번호만으로 발급된 Refresh Token으로 `amr=mfa` 클레임이 붙은 Access Token을 받을 수 있었다. TOTP 인증을 거치지 않은 세션이 MFA를 거친 것처럼 표시되는 문제다.

개선:

enable 성공 시 SecurityStamp 갱신 + 사용자 Refresh Token 전체 폐기. 클라이언트는 활성화 후 다시 로그인해 TOTP 2차 인증을 거쳐야 한다.

회귀 테스트: `RegressionTests.TwoFactorEnable_RevokesPasswordOnlyRefreshTokens`

---

## 정리: SecurityStamp 변경과 Refresh Token 폐기

REG-004, REG-005, REG-007 이후 SecurityStamp를 변경하는 모든 경로는 사용자의 Refresh Token도 함께 폐기한다.

- 비밀번호 변경
- 비밀번호 재설정
- 2FA 활성화
- 2FA 비활성화
- Authenticator 초기화
- 관리자 2FA 초기화
- 전체 기기 로그아웃 (사용자 `/api/account/sessions/revoke-all`, 관리자 `/api/admin/users/sessions/revoke-all`)

SecurityStamp를 변경하는 새 기능을 추가할 때도 `RefreshTokenService.RevokeAllAsync`를 함께 호출해야 한다.

---

## REG-008 2FA Challenge Token으로 2차 인증 우회 (심각)

문제:

`/api/auth/login`이 2FA 사용자에게 주는 `challengeToken`이 Access Token과 같은 서명 키, Issuer, Audience로 발급되었다. 여기에 `sub`, `nameidentifier`, `security_stamp` 클레임까지 같았다.

결과:

비밀번호만 아는 공격자가 `challengeToken`을 `Authorization: Bearer`로 보내면 TOTP 없이 `/api/account/**` 전체를 호출할 수 있었다. 2FA가 사실상 무력화된 상태였다. 실제 서버에서 Web E2E 시나리오로 발견했다(`/api/account/me` → 200).

개선:

- challenge token은 별도 Audience(`Jwt:TwoFactorAudience`, 기본값 `<Jwt:Audience>/2fa`)로 발급해 Bearer 검증 단계에서 거부되게 한다.
- `OnTokenValidated`는 `purpose` 클레임이 있는 토큰을 Access Token으로 받지 않는다(방어 심층화).

회귀 테스트: `RegressionTests.ChallengeToken_IsNotAcceptedAsAccessToken` (`/me`, `/2fa/status`, `/2fa/recovery-codes/regenerate`)

---

## REG-009 2FA Setup 직후 Enable 불가

문제:

`/api/account/2fa/setup`이 키 생성에 `UserManager.ResetAuthenticatorKeyAsync`를 썼다. 이 메서드는 내부에서 SecurityStamp도 바꾼다.

결과:

setup을 호출한 Access Token이 즉시 무효가 되었다. 그래서 바로 이어지는 `/2fa/enable`이 항상 401이었고, Authenticator 등록 자체가 불가능했다.

개선:

아직 2FA가 꺼진 상태이므로 `IUserAuthenticatorKeyStore`에 키만 저장하고 SecurityStamp는 유지한다. 세션 무효화는 `/enable`에서 일어난다(REG-007).

회귀 테스트: 2FA를 등록하는 모든 테스트(`AuthFlowTests.Totp_SetupEnableLogin_AndRecoveryCodeIsOneTime` 등)

---

## REG-010 정상 폐기 토큰이 재사용 공격으로 기록됨

문제:

`RotateAsync`가 폐기된 토큰이면 폐기 사유와 관계없이 재사용 공격으로 판정했다. 로그아웃, 비밀번호 변경, 2FA 변경, 관리자 초기화로 폐기된 토큰도 모두 해당했다.

결과:

정상 사용 중에도 `refresh.reuse`(탈취 의심) 감사 이벤트가 대량으로 쌓였다. E2E 1회당 회전 재사용 1건에 비해 오탐이 약 9건이었다. 그래서 실제 탈취 탐지 경보로 쓸 수 없었다.

개선:

`RevokeReason`이 `rotated`인 토큰과 동시 회전 충돌만 `refresh.reuse`로 기록하고 family를 폐기한다. 그 밖의 폐기 토큰은 `refresh.revoked`(`Detail`=폐기 사유)로 기록한다. 이런 토큰의 family에는 활성 토큰이 남아 있지 않으므로 보안 효과는 같다.

회귀 테스트: `RegressionTests.AuditLog_RefreshWithLoggedOutToken_IsRevokedNotReuse`, `AuditLog_RefreshAfterPasswordChange_IsRevokedNotReuse` (기존 `AuditLog_RefreshReuse_RecordsTokenOwner`, `Refresh_Rotates_AndReuseRevokesFamily`, `Refresh_SameTokenConcurrent_AtMostOneSucceeds` 유지)

---

## REG-011 VB6 샘플이 2FA 사용자를 인식하지 못함

문제:

`VB6Sample/modIdentityApi.bas`의 JSON 추출 함수에 결함이 두 개 있었다.
- `JsonBool`: `:` 뒤 5글자를 잘라 `"true"`와 비교했다. 그래서 `"requiresTwoFactor":true,`는 `true,`가 되어 항상 False였다.
- `JsonString`: 값이 `null`이면 그 뒤에 나오는 다른 속성의 문자열을 값으로 읽었다.

결과:

2FA 사용자가 로그인하면 샘플이 TOTP를 묻지 않았다. 대신 잘못된 값을 토큰으로 저장한 채 "로그인 성공"을 표시했다. 이 모듈을 그대로 업무 프로그램에 옮기면 2FA 로그인이 동작하지 않는다.

개선:

`JsonBool`은 값의 앞 4글자를 비교하고, `JsonString`은 문자열이 아닌 값(`null` 등)이면 빈 문자열을 돌려준다. 샘플은 바로 열 수 있는 VB6 프로젝트(`IdentityApiSample.vbp`, `frmLogin.frm`)로 정리했다. 함께 바꾼 점은 다음과 같다.
- 실패 시 `gLastStatus`로 HTTP 상태를 안내한다.
- 401이면 Refresh를 1회만 재시도한다.
- 423(잠금)은 따로 안내한다.

회귀 테스트: `tools/Vb6SampleCheck`. `modIdentityApi.bas`를 그대로 포함해 실제 API로 검증하며(2026-10-02 기준 12개 항목), `scripts/run-e2e.ps1`에 포함되어 있다.

---

## REG-012 긴 User-Agent로 로그인하면 500

문제:

`AuditService`가 요청의 User-Agent를 길이 제한 없이 `AuditLogs.UserAgent`(`nvarchar(512)`)에 저장했다.

결과:

SQL Server에서는 User-Agent가 512자를 넘으면 감사 로그 저장이 실패한다. 감사 로그는 로그인·Refresh 등 모든 인증 API에서 쓰므로, 그런 클라이언트는 로그인 자체가 500이 되었다. InMemory DB는 길이를 검사하지 않아 기존 테스트로는 드러나지 않았다. 세션 관리 기능을 만들면서 User-Agent를 다루다가 발견했다(2026-10-02).

개선:

`AuditService`가 User-Agent는 512자, `Detail`은 2000자로 잘라 저장한다. 세션에 저장하는 User-Agent(`RefreshTokens.UserAgent`)도 512자로 자른다.

회귀 테스트: `RegressionTests.Login_WithVeryLongUserAgent_Succeeds`. SQL Server 모드에서 수정 전 코드로 실패하고 수정 후 통과하는 것을 확인했다.

---

## REG-013 회전과 동시에 폐기하면 폐기가 실패 (보안)

문제:

로그아웃, 비밀번호 변경, 2FA 변경, 관리자 초기화의 Refresh Token 폐기(`RevokeAsync`, `RevokeAllAsync`)는 토큰을 읽고 `Version`을 바꿔 저장한다. 읽은 뒤 저장하기 전에 같은 토큰이 회전되면 `DbUpdateConcurrencyException`이 그대로 던져졌다. v4 원본부터 있던 결함이며, 같은 방식으로 만든 새 세션 폐기 API도 마찬가지였다.

결과:

API는 500을 반환하고, 저장이 원자적이라 **아무 토큰도 폐기되지 않았다**. 훔친 Refresh Token으로 Refresh를 반복하는 공격자가 있으면, 피해자가 비밀번호를 바꾸거나 전체 로그아웃을 해도 공격자의 새 토큰이 살아남는다. SecurityStamp는 먼저 바뀌므로 Access Token은 끊기지만, Refresh로 새 stamp의 Access Token을 다시 받을 수 있다(REG-004 우회). 독립 코드 리뷰에서 지적되었다(2026-10-02).

개선:

- 폐기는 활성(폐기·만료되지 않은) 토큰만 대상으로 하고, 동시성 충돌이 나면 다시 읽어 재시도한다. 다시 읽으면 회전으로 생긴 새 토큰도 대상에 들어간다(`RefreshTokenService.RevokeActiveAsync`).
- 로그아웃은 제출한 토큰의 세션(family) 전체를 폐기한다. 보통은 그 토큰 하나다.
- 충돌 처리에서 `ChangeTracker.Clear()`를 쓰지 않는다. 같은 컨텍스트의 사용자 엔티티까지 떼어 내기 때문이다. RefreshToken 추적만 푼다.

회귀 테스트: `RevokeRaceTests.Revoke_WhenTokenIsRotatedConcurrently_StillRevokesTheRotatedToken`(logout / session / revoke-all / change-password). 테스트 DbContext의 `BeforeSaveHook`으로 폐기 저장 직전에 다른 컨텍스트에서 실제 회전을 일으켜 확정적으로 재현한다. 수정 전 4건 모두 실패, 수정 후 통과.

같은 리뷰에서 함께 고친 점:
- 만료 토큰 정리가 살아 있는 세션의 오래된 회전 토큰까지 지우면, 그 토큰이 다시 들어왔을 때 재사용 탐지가 동작하지 않았다. 세션의 모든 토큰이 보존 기간을 넘겨 만료된 경우에만 지운다(`SessionTests.Purge_KeepsOldRotatedTokenOfLiveFamily_SoReuseIsStillDetected`).
- 정리 작업이 여러 인스턴스에서 같은 행을 지우다 충돌하면 그 회차를 중단했다. 충돌한 배치를 다시 조회해 계속한다.
- 메일 발송이 영구 오류(5xx, 없는 주소 등)도 재시도하며 단일 작업자를 수 분씩 붙잡았다. 영구 오류는 바로 `email.failed`로 남기고, 작업자를 여러 개(`Email:Smtp:MaxConcurrency`, 기본 4) 둔다(`SmtpEmailTests.PermanentSmtpFailure_IsNotRetried_AndIsAudited`).

---

## 2026-10-02 기능 추가 중 테스트 변경과 발견 사항

- `RegressionTests.AdminReset2Fa_Disables2Fa`: 관리자 API가 MFA 세션을 요구하게 되었으므로(40장), 관리자가 2FA를 켜고 TOTP로 로그인하도록 준비 단계만 바꿨다. 검증 내용(Assert)은 그대로다.
- SMTP 재시도 설정(`Email:Smtp:RetryDelaysSeconds`)을 속성 초기값으로 기본값을 두었더니, 설정 바인더가 설정값을 기본값 뒤에 덧붙였다(`5,30,120,0.2,0.2`). 배포 전 테스트(`SmtpDownTests`)에서 발견해 기본값을 사용 시점에 적용하도록 고쳤다. 회귀 테스트: `SmtpEmailTests.RetryDelays_FromConfig_ReplaceDefaults`. 배열 설정을 추가할 때 같은 함정에 주의한다.

---

## 최초 실제 빌드·실행에서 발견한 결함 (2026-10-01)

v4는 .NET SDK와 SQL Server 없이 작성되었다. 처음으로 .NET 10 SDK와 SQL Server 2022 Express에서 빌드·실행하면서 다음 결함을 찾아 수정했다.

| 구분 | 결함 | 영향 | 수정 |
|---|---|---|---|
| DB | `001` 스크립트가 `sqlcmd` 기본값 `QUOTED_IDENTIFIER OFF`에서 필터 인덱스 생성에 실패 | CI 포함 **모든 `sqlcmd` 적용이 실패**(트랜잭션 롤백) | 스크립트 첫머리에 `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;` |
| 빌드 | `.sln`에 `Release` 구성 매핑 누락 | `-c Release` 빌드·테스트가 **아무 프로젝트도 실행하지 않고 성공**으로 끝남(CI, `run-regression.ps1`) | Release 매핑 추가 |
| 빌드 | Swashbuckle 10(Microsoft.OpenApi 2.x)에서 `Microsoft.OpenApi.Models` 제거됨 | 컴파일 오류 | OpenApi 2.x API로 수정 |
| 보안 | Microsoft.OpenApi 2.3.0 높음 심각도 취약점(GHSA-v5pm-xwqc-g5wc) | 취약 패키지 사용 | Swashbuckle 10.2.3 + Microsoft.OpenApi 2.12.2 |
| 빌드 | `TwoFactorController.IdentityError`가 `ActionResult<T>`와 호환되지 않음 / 테스트의 `using` 누락 | 컴파일 오류 | 반환 타입·using 수정 |
| 테스트 | `ApiFactory`가 InMemory DB 이름을 `AddDbContext` 람다 안에서 생성 | 요청 scope마다 다른 DB가 되어 거의 모든 테스트가 실패 | DB 이름을 factory당 1회 생성 |
| 인증 | `Program.cs`가 `Jwt:Key`를 호스트 빌드 전에 읽음 | 나중에 추가된 설정(테스트 등)과 서명 키가 어긋나 **모든 Access Token이 401** | 옵션 구성 시점에 읽도록 변경 |
| 인증 | REG-009 | Authenticator 등록 불가 | 위 참조 |
| 보안 | REG-008 | 2FA 우회 | 위 참조 |

특히 `.sln`과 `ApiFactory` 결함 때문에, 기존 테스트 일부는 "통과"로 보였지만 실제로는 아무것도 검증하지 않았다. 예를 들어 SecurityStamp 테스트는 원래부터 401이 나와서 우연히 통과했다.

---

# 23. VB6 Client 연동

VB6에서는 `WinHttp.WinHttpRequest.5.1`을 이용하여 REST API를 호출한다.

전체 흐름:

```text
VB6
 |
 +--> ApiLogin()
 |       |
 |       +-- 2FA OFF --> Token 저장
 |       |
 |       +-- 2FA ON
 |              |
 |              v
 |          ApiTotp()
 |              |
 |              v
 |          Token 저장
 |
 +--> 업무 API 호출
 |
 +--> Access Token 만료
 |       |
 |       v
 |   ApiRefresh()
 |       |
 |       v
 |   신규 Access/Refresh 저장
 |
 +--> ApiLogout()
         |
         v
    Refresh Token 폐기
```

VB6에서는 JSON 파싱 라이브러리를 별도로 사용하는 것이 권장된다.

Token 문자열을 로그 파일이나 화면에 그대로 출력하지 않는다.

---

# 24. VB6 호출 시 주의사항

1. HTTPS만 사용
2. Access Token/Refresh Token 평문 로그 금지
3. Refresh Token Rotation 후 반드시 새 Refresh Token으로 교체
4. 이전 Refresh Token 재사용 금지
5. 401 발생 시 무조건 Refresh하는 구조 금지
6. Refresh 실패 시 Login 화면으로 복귀
7. TOTP Challenge Token과 Access Token을 구분
8. 서버 인증서 검증을 임의로 끄지 않음

---

# 25. 감사 로그

보안 관련 이벤트를 Audit Log로 남기는 구조를 사용한다.

현재 구현된 이벤트 (`AuditLogs.EventType`):

| 분류 | EventType | 발생 시점 |
|---|---|---|
| 가입 | `register.success` | 회원가입 |
| 로그인 | `login.success` | 2FA 미사용자 로그인 성공(토큰 발급) |
| | `login.2fa-required` | 비밀번호 성공, 2FA Challenge 발급 |
| | `login.failed` | 존재하지 않는 사용자, 비밀번호 오류 |
| | `login.locked` | 이미 잠긴 계정의 로그인 시도 |
| 잠금 | `account.locked` | 이번 실패로 계정이 잠김 (로그인·TOTP·복구 코드 실패 공통, 잠길 때 1회) |
| 2차 인증 | `2fa.success` / `2fa.failed` / `2fa.locked` | TOTP 2차 인증 성공 / 실패 / 잠긴 계정 |
| | `recovery.success` / `recovery.failed` / `recovery.locked` | 복구 코드 로그인 성공 / 실패 / 잠긴 계정. 성공 시 `Detail`에 남은 복구 코드 수 |
| 2FA 관리 | `2fa.setup` | Authenticator 등록 정보 발급 |
| | `2fa.enable` / `2fa.enable.failed` | 2FA 활성화 / TOTP 오류 |
| | `2fa.disable` / `2fa.disable.failed` | 2FA 비활성화 / 비밀번호·TOTP 오류 (`Detail`: `password` 또는 `code`) |
| | `2fa.reset` / `2fa.reset.failed` | Authenticator 초기화 / 비밀번호·TOTP 오류 |
| | `2fa.recovery-codes.regenerate` / `.failed` | 복구 코드 재발급 / TOTP 오류 |
| 토큰 | `refresh.success` / `refresh.failed` | Refresh 성공 / 실패(`Detail`: `unknown`, `expired`, `user-missing`) |
| | `refresh.reuse` | **탈취 의심.** 이미 회전된 토큰의 재사용(family 전체 폐기), 또는 동시 회전 충돌(`Detail`: `concurrent`) |
| | `refresh.revoked` | 정상적으로 폐기된 토큰으로 갱신 시도. `Detail`: 폐기 사유(`user-revoke`, `password-change`, `password-reset`, `2fa-enable`, `2fa-disable`, `2fa-reset`, `admin-2fa-reset`, `refresh-token-reuse`, `session-revoke`, `session-revoke-others`, `session-revoke-all`, `admin-session-revoke-all`) |
| | `logout` / `logout.failed` | Refresh Token 폐기 / 없거나 이미 폐기된 토큰 |
| 세션 | `session.revoke` | 특정 기기 로그아웃 (`Detail`: sessionId) |
| | `session.revoke-others` / `session.revoke-all` | 다른 기기 모두 / 전체 기기 로그아웃 (`Detail`: `revokedSessions=n`) |
| 비밀번호 | `password.change` | 비밀번호 변경 |
| | `password.reset` | 비밀번호 재설정 |
| 관리자 | `admin.2fa.reset` | 관리자 2FA 초기화 (사유는 `Detail`에 기록) |
| | `admin.sessions.read` / `admin.sessions.revoke-all` | 대상 사용자 세션 조회 / 전체 기기 로그아웃 (사유는 `Detail`) |
| | `admin.audit.read` | 감사 로그 조회. `UserId`=조회한 관리자, `Detail`=조회 조건과 결과 수 |
| | `admin.denied` | 관리자 API 거부(403). `UserId`=호출자, `Detail`=`not-admin` 또는 `mfa-required` |
| 메일 | `email.failed` | 재시도까지 모두 실패한 메일. `UserId`=수신자 계정(있으면), `Detail`=메일 제목 |

`ActorUserId` 열(003 스크립트)은 요청을 보낸 인증된 사용자다. 관리자 작업이면 `UserId`는 대상 사용자, `ActorUserId`는 관리자다. 본인 작업이면 둘이 같고, 로그인 전 요청이나 백그라운드 작업이면 비어 있다.

보안 모니터링 경보는 `refresh.reuse`에만 건다. `refresh.revoked`는 로그아웃한 앱이 예전 토큰으로 재시도하는 등 정상 상황에서도 발생한다(REG-010).

`refresh.failed`, `refresh.reuse`, `refresh.revoked`에는 제출된 토큰 소유자의 `UserId`가 기록된다. DB에 없는 토큰이면 비어 있다. `logout.failed`와 존재하지 않는 사용자의 `login.failed`는 `UserId`가 비어 있다.

사용자별 조회를 위해 `IX_AuditLogs_UserId_OccurredAt` 인덱스를 둔다(`MigrationsSql/002_AuditLogs_UserId_Index.sql`).

회귀 테스트:

- `RegressionTests.AuditLog_RecordsLoginTwoFactorRecoveryAndLogoutEvents`: 이벤트가 기록되는지와 함께, `Detail`에 Refresh Token·TOTP Secret·복구 코드 원문이 없는지 확인한다.
- `RegressionTests.AuditLog_RecordsTwoFactorResetAndRecoveryCodeRegeneration`
- `RegressionTests.AuditLog_RecordsAccountLockedOnceAndLockedRejections`
- `RegressionTests.AuditLog_RefreshReuse_RecordsTokenOwner`

감사 로그에는 Access Token, Refresh Token, Password, TOTP Secret 원문을 기록하지 않는다.

---

# 26. CI/CD

GitHub Actions 기준 다음 순서로 구성한다.

```text
Checkout
   |
   v
SQL Server 2022 Service
   |
   v
DB 준비 대기
   |
   v
고정 Migration SQL 적용
   |
   v
dotnet restore
   |
   v
dotnet build -c Release
   |
   v
dotnet test
   |
   v
TRX 결과 저장
```

SQL 적용은 실패를 무시하지 않도록 `sqlcmd -b` 사용을 권장한다.

테스트 실패 시 CI Pipeline도 실패해야 한다.

---

# 27. 로컬 Regression 실행

Windows 개발 환경에서는 제공된 PowerShell Script를 이용하여 동일한 회귀 테스트 절차를 실행하도록 구성한다.

예:

```powershell
.\scripts\run-regression.ps1
```

목표는 개발자 PC와 CI의 테스트 절차 차이를 최소화하는 것이다.

---

# 28. 테스트 결과 보존

CI에서 테스트 실패가 발생하더라도 TRX 등 결과 파일은 Artifact로 보존해야 한다.

필요 시 다음 정보도 보존한다.

- TRX
- 서버 로그
- 테스트 이름
- Exception
- Stack Trace
- SQL 오류
- HTTP Request ID / Correlation ID

단, Password/Token/Authenticator Secret은 로그에서 제거한다.

---

# 29. 운영 환경 Secret 관리

다음 값은 소스 저장소에 실제 값을 넣지 않는다.

- JWT Signing Key
- SMTP Password
- DB Password
- 외부 서비스 API Key

운영에서는 환경 변수, Secret Manager, Key Vault/HSM 등 별도 Secret Store 사용을 권장한다.

---

# 30. 운영 전 필수 점검

## 보안

- HTTPS 강제
- HSTS
- JWT Signing Key 교체
- DB 계정 최소 권한
- CORS Allow List
- Rate Limit
- NTP 시간 동기화
- Reverse Proxy 설정
- Forwarded Headers
- Secret 외부화
- 로그 Token Masking

## DB

- Migration Test DB 검증
- 운영 Backup
- Rollback Script 검증
- Index 확인
- Refresh Token Cleanup 정책

## 인증

- 이메일 확인
- 비밀번호 변경
- 비밀번호 재설정
- TOTP Setup/Enable
- Recovery Code
- 관리자 Reset
- Logout
- Refresh Rotation
- Refresh Reuse Detection

---

# 31. 시간 동기화

TOTP는 시간 기반 인증 방식이므로 서버 시간이 정확해야 한다.

운영 서버는 NTP 시간 동기화를 활성화한다.

서버 시간이 크게 틀어지면 정상 Authenticator 코드도 실패할 수 있다.

---

# 32. 현재 검증 상태

2026-10-01 로컬 개발 PC에서 실제로 실행해 확인했다. CI는 GitHub Actions에서 확인했다.

검증 환경: Windows 10, .NET SDK 10.0.401, SQL Server 2022 Express(`localhost\SQLEXPRESS`, DB `IdentityTotp_Test`), Node 22, Chrome, Visual Basic 6.0

| 항목 | 결과 |
|---|---|
| SQL Migration (`001`, `002`) 적용 / 재적용(idempotent) / `002` rollback 후 재적용 | 성공 |
| `dotnet build -c Release` | 경고 0, 오류 0 |
| xUnit (InMemory) | 26/26 통과 |
| xUnit (SQL Server, 반복 실행) | 26/26 통과 (동시 Refresh 포함, 불안정 없음) |
| E2E Web 시나리오 (Node) | 44/44 통과 |
| E2E Web 시나리오 (헤드리스 Chrome, `index.html`) | 44/44 통과, 콘솔 오류 없음 |
| E2E VB6 시나리오 (컴파일된 `Vb6TestClient.exe`, TOTP를 VB6 CryptoAPI로 계산) | 95/95 통과 |
| VB6 샘플 모듈 (`VB6Sample/modIdentityApi.bas`, `tools/Vb6SampleCheck`) | 11/11 통과 |
| VB6/JS TOTP 계산 | RFC 6238 테스트 벡터 6개 일치 |
| 감사 로그 | 전체 이벤트 기록, `Detail`에 토큰 원문 없음 |
| GitHub Actions CI (`ci.yml`, ubuntu-latest + SQL Server 2022 컨테이너) | 성공. Migration 적용, Release 빌드, SQL Server 테스트, TRX Artifact 업로드 전 단계 통과 ([run #1](https://github.com/loviswow/IdentityTotpSelfServiceApi/actions/runs/36848395947), `3420c69`) |

2026-10-02 관리자 보안·세션 관리·SMTP 추가 후 다시 확인했다(같은 PC).

| 항목 | 결과 |
|---|---|
| SQL Migration `003` 적용 / 재적용 / rollback / rollback 재실행 / 재적용 | 성공 |
| `dotnet build -c Release` | 경고 0, 오류 0. `dotnet list package --vulnerable` 취약 패키지 없음(MailKit 4.18.1 추가) |
| xUnit (InMemory, 반복 실행) | 58/58 통과 |
| xUnit (SQL Server, 반복 실행) | 58/58 통과 |
| REG-012, REG-013 재현 | 수정 전 코드로 실패(REG-012는 SQL Server), 수정 후 통과 |
| SMTP | MailKit으로 테스트 SMTP 서버(xUnit 내장)에 실제 SMTP 대화(EHLO/AUTH PLAIN/MAIL/RCPT/DATA)로 전달. 일시 장애 재시도, 서버 다운 시 API 무영향과 `email.failed` 기록 |
| E2E Web (Node / 헤드리스 Chrome) | 각 52/52 통과 (세션 5단계, 관리자 MFA 로그인·감사 조회·세션 관리 포함) |
| E2E VB6 (`Vb6TestClient.exe`) | 139/139 통과 |
| VB6 샘플 모듈 (`tools/Vb6SampleCheck`) | 12/12 통과 (deviceName → 세션 목록 표시 포함) |
| 감사 로그 | 새 이벤트(`session.*`, `admin.*`) 기록, `Detail`에 토큰 원문 없음 |

E2E 재실행 방법은 `scripts/run-e2e.ps1`, 테스트 클라이언트 사용법은 `tools/README.md`를 참고한다.

아직 확인되지 않은 항목:

```text
Authenticator 실제 기기(휴대폰 앱) 테스트
실제 SMTP 서버로 메일 발송·수신 (SMTP 구현은 테스트 SMTP 서버로 검증. 운영 SMTP 계정 정보가 있어야 확인 가능)
실제 VB6 업무 프로그램 연동 (테스트 클라이언트로만 검증)
운영 Reverse Proxy/HTTPS/Forwarded Headers 테스트
운영 Secret 외부화, DB Backup/Rollback 절차
```

---

# 33. 권장 검증 순서

후속 담당자는 다음 순서로 진행한다.

```text
1. .NET 10 SDK 설치/확인
2. SQL Server 준비
3. Connection String 설정
4. JWT Secret 설정
5. 고정 Migration SQL 적용
6. dotnet restore
7. dotnet build -c Release
8. dotnet test
9. 실패 Test 분석
10. 재현 Test 유지
11. 코드 수정
12. 관련 Test 재실행
13. 전체 Regression Test
14. Swagger 수동 Smoke Test
15. 실제 Authenticator 기기 Test
16. VB6 연동 Test
17. 운영 배포 전 보안 점검
```

---

# 34. 테스트 실패 처리 규칙

테스트 실패 시 테스트를 억지로 통과시키기 위해 Assert를 완화하지 않는다.

다음 순서로 판단한다.

1. 요구사항이 잘못되었는지 확인
2. 테스트 구현이 잘못되었는지 확인
3. 실제 코드 결함인지 확인
4. DB Schema 문제인지 확인
5. 동시성/시간 의존 문제인지 확인
6. 수정
7. 실패 Test 재실행
8. 전체 회귀 실행

TOTP 테스트는 시간 의존성이 있으므로 시스템 시간과 허용 Window를 특히 확인한다.

---

# 35. 향후 개선 우선순위

## 우선순위 1

실제 .NET + SQL Server 환경에서 전체 Build/Test 실행 및 실패 수정.

## 우선순위 2

실제 SMTP 또는 사내 메일 시스템 연결.

→ 2026-10-02 SMTP 발송 구현(41장). 운영 SMTP 서버로 실제 수신 확인만 남았다.

## 우선순위 3

VB6 실제 프로그램에서 End-to-End 테스트.

```text
VB6 Login
 -> TOTP
 -> 업무 API
 -> Access 만료
 -> Refresh
 -> Logout
```

## 우선순위 4

관리자 보안 기능 강화.

- 관리자 API 별도 Policy
- 관리자도 MFA 필수
- 관리자 2FA Reset 사유 기록
- 관리자 Audit 강화

→ 2026-10-02 구현(40장).

## 우선순위 5

Refresh Token 관리 강화.

- 만료 Token 정리 Job
- Device/Session 관리
- 사용자별 로그인 기기 조회
- 특정 기기 강제 Logout
- 전체 기기 Logout

→ 2026-10-02 구현(39장).

---

# 36. 프로젝트 인수 시 핵심 확인 파일

v4 프로젝트에서 다음 항목을 우선 확인한다.

```text
IdentityTotpSelfServiceApi_v4/
├── IdentityTotpSelfServiceApi.sln
├── REGRESSION_WORKFLOW.md
├── .github/workflows/ci.yml
├── scripts/run-regression.ps1
├── IdentityTotpSelfServiceApi/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Controllers/
│   ├── Services/
│   ├── Models/
│   ├── Entities/
│   ├── Dtos/
│   ├── Data/
│   ├── MigrationsSql/
│   ├── VB6Sample/
│   ├── README.md
│   └── TEST_CASES.md
└── IdentityTotpSelfServiceApi.Tests/
```

---

# 37. 최종 인수 기준

프로젝트를 운영 가능 상태로 인수 완료하려면 최소한 다음 조건을 충족해야 한다.

`[x]`는 2026-10-01 로컬 검증(32장)으로 확인한 항목이다. 운영 환경에서 다시 확인해야 한다.

```text
[x] SQL Migration 성공
[x] Release Build 성공
[x] 전체 xUnit Test 성공
[ ] TOTP 실제 기기 인증 성공
[x] Recovery Code 성공/재사용 실패
[x] Refresh Rotation 성공
[x] Refresh Reuse Detection 성공
[x] 동시 Refresh 테스트 성공
[x] Logout 후 Refresh 실패
[x] Password 변경 후 기존 Access 실패
[x] Password 변경 후 기존 Refresh 실패
[x] 2FA Enable/Disable/Reset 후 기존 Refresh 실패
[x] 2FA Challenge Token을 Access Token으로 사용 불가 (REG-008)
[x] TOTP/Recovery Code 반복 실패 시 423 Lock
[x] 관리자 2FA Reset 성공
[x] 일반 사용자 Admin API 403
[x] 비밀번호만으로 로그인한 관리자 Admin API 403 (MFA 필수)
[x] 특정 기기 / 다른 기기 / 전체 기기 로그아웃 후 해당 Access·Refresh 실패
[x] Rate Limit 429 확인 (xUnit)
[ ] 이메일 확인/재설정 실제 발송 성공   (SMTP 구현 완료, 테스트 SMTP 서버로 검증. 운영 SMTP로 실제 수신 확인 필요)
[x] VB6 Login/TOTP/Refresh/Logout 성공   (VB6 테스트 클라이언트. 실제 업무 프로그램은 미검증)
[x] 감사 로그 확인
[ ] 운영 Secret 외부화
[ ] DB Backup/Rollback 검증
```

모든 항목이 확인되기 전까지 운영 검증 완료로 간주하지 않는다.

---

# 38. 결론

본 프로젝트의 핵심은 단순 TOTP 구현이 아니라 다음 인증 수명주기 전체를 REST API로 안전하게 처리하는 것이다.

```text
Password
   +
TOTP
   +
Short-lived Access JWT
   +
Refresh Token Rotation
   +
Reuse Detection
   +
SecurityStamp
   +
Recovery Code
   +
Rate Limit
   +
Audit
   +
Regression Test
```

향후 변경 시에도 보안 결함을 발견하면 반드시 재현 테스트를 먼저 남기고 수정 후 전체 회귀 테스트를 수행하는 원칙을 유지한다.

---

# 39. 세션(기기) 관리와 만료 토큰 정리 (2026-10-02)

35장 우선순위 5를 구현했다.

## 세션 모델

```text
로그인(/login, /2fa, /2fa/recovery)
   |
   +-- Refresh Token 발급 (새 FamilyId = 세션 ID, DeviceName, UserAgent 저장)
   +-- Access Token 발급 (sid = FamilyId)

Refresh 회전 → 같은 FamilyId 안에서 새 토큰. DeviceName/UserAgent를 이어받는다.
세션이 "활성" = family에 폐기되지 않고 만료되지 않은 토큰이 있다.
```

- 세션 ID는 FamilyId(32자 hex)다. 토큰이나 해시가 아니므로 응답에 내보내도 된다.
- `OnTokenValidated`는 SecurityStamp 확인에 더해 `sid` 세션이 활성인지 확인한다(`RefreshTokenService.IsSessionActiveAsync`). 요청마다 인덱스(`IX_RefreshTokens_UserId_FamilyId`) 조회가 하나 늘어난다.
- `sid`가 없는 이전 버전 Access Token은 세션 확인을 건너뛴다. 배포 직후에도 기존 로그인이 끊기지 않는다(최대 Access Token 수명 15분 동안).

## API

| API | 동작 | 폐기 사유(`RevokeReason`) |
|---|---|---|
| `GET /api/account/sessions` | 활성 세션 목록. `current`=지금 요청한 세션 | - |
| `DELETE /api/account/sessions/{id}` | 특정 기기 로그아웃. 남의 세션·없는 세션은 404 | `session-revoke` |
| `POST /api/account/sessions/revoke-others` | 현재 세션 외 모두 로그아웃 | `session-revoke-others` |
| `POST /api/account/sessions/revoke-all` | 전체 로그아웃 + SecurityStamp 갱신 | `session-revoke-all` |
| `POST /api/admin/users/sessions/revoke-all` | 관리자에 의한 전체 로그아웃 + SecurityStamp 갱신 | `admin-session-revoke-all` |

폐기 사유에는 `rotated`를 쓰지 않는다. 그래야 폐기된 토큰이 다시 들어와도 `refresh.revoked`로만 기록되고 재사용 공격 경보가 울리지 않는다(REG-010).

## 만료 토큰 정리

`RefreshTokenCleanupService`(BackgroundService)가 `RefreshTokens:Cleanup:IntervalMinutes`(기본 60분)마다 만료 후 `RetentionDays`(기본 30일)가 지난 토큰을 `BatchSize`(기본 500)씩 지운다. 기동 30초 뒤 처음 실행한다.

- 회전된 토큰이 남아 있어야 재사용 탐지가 된다. 그래서 폐기 시각이 아니라 **만료 시각** 기준으로 지우고, 세션(family)의 **모든** 토큰이 보존 기간을 넘겨 만료된 경우에만 지운다. 살아 있는 세션의 오래된 회전 토큰은 남긴다(REG-013 항목 참고).
- 정리 쿼리용으로 `IX_RefreshTokens_ExpiresAt` 인덱스를 둔다(003).
- 여러 인스턴스가 같은 행을 지우다 동시성 충돌이 나면 그 배치를 다시 조회해 계속한다.

---

# 40. 관리자 보안 강화 (2026-10-02)

35장 우선순위 4를 구현했다.

## 정책 (`Services/AdminAuthorization.cs`)

관리자 API(`AdminController`)는 `[Authorize(Policy = AdminPolicy.Name)]`이다. `AdminMfaHandler`가 요청마다 다음을 확인한다.

1. **Admin 역할을 DB에서 확인한다.** 토큰의 role 클레임은 발급 시점 값이므로 역할을 회수해도 남는다. DB를 보면 회수 즉시 거부된다.
2. **2차 인증 세션(`amr=mfa`)이어야 한다.** 비밀번호만 아는 공격자는 관리자 API를 쓸 수 없다.
3. **지금도 2FA가 켜져 있어야 한다.**

거부(403)는 `AdminAuthorizationResultHandler`가 `admin.denied`(`Detail`=`not-admin`/`mfa-required`)로 기록하고, 본문에 `reason`을 담아 응답한다.

## 운영 영향

- **기존 관리자는 2FA를 켜야 한다.** 배포 후 비밀번호만으로 로그인한 관리자는 관리자 API에서 403(`mfa-required`)을 받는다. 배포 전에 관리자 계정의 Authenticator 등록(`/2fa/setup` → `/2fa/enable`)을 안내한다.
- 관리자 작업에는 사유가 필수다(앞뒤 공백 제외 1~500자).
- 관리자 API로 자기 계정의 2FA는 초기화할 수 없다(400). self-service API로 비밀번호+TOTP를 확인하고 바꾼다.

## 감사 강화

- `AuditLogs.ActorUserId`(003)에 요청한 사용자를 자동 기록한다. 관리자 작업이면 `UserId`=대상, `ActorUserId`=관리자다.
- 감사 로그 조회 API(`GET /api/admin/audit-logs`)를 추가했다. 조회 자체도 `admin.audit.read`로 남긴다.
- 대상 사용자 세션 조회·전체 로그아웃도 기록한다(`admin.sessions.read`, `admin.sessions.revoke-all`).

---

# 41. 메일 발송 (SMTP) (2026-10-02)

35장 우선순위 2를 구현했다. 설정 키는 API README "메일 발송 (SMTP)"에 있다.

## 구조

```text
API 요청 (register / resend-confirmation / forgot-password)
   |
   v
EmailQueue (메모리 Channel, 최대 10,000건)  ← 요청은 여기서 바로 반환
   |
   v
EmailDispatchService (BackgroundService)
   |
   v
SmtpEmailSender (MailKit) --실패--> RetryDelaysSeconds 간격 재시도 --최종 실패--> 오류 로그 + email.failed
```

발송기 선택(`Program.cs`, 요청 시점 설정 기준):

| 조건 | 발송기 |
|---|---|
| `Email:Smtp:Host` 있음 | `EmailQueue` → SMTP |
| Development + `Email:PickupDirectory` | 파일 저장 (E2E) |
| 그 밖 | 로그만 남김. 운영 환경이면 기동 시 "메일이 발송되지 않는다" 경고 |

## 설계 이유

- **계정 존재 여부 노출 방지.** `forgot-password`는 계정이 있을 때만 메일을 보낸다. SMTP 응답을 기다리면 계정이 있을 때만 응답이 느려져 시간 차이로 계정 존재를 알 수 있다. 큐에 넣고 바로 응답해 이 차이를 없앴다.
- **장애 격리.** SMTP 장애가 회원가입·재설정 API의 500으로 번지지 않는다.
- **유실 가능성.** 큐는 메모리에만 있어 프로세스가 재시작되면 보내지 못한 메일이 사라진다. 사용자는 재발송 API로 다시 요청할 수 있다. 유실이 허용되지 않으면 DB Outbox 테이블로 바꾼다.

## 보안

- `Email:Smtp:Password`는 환경 변수(`Email__Smtp__Password`)나 Secret Store로만 준다(29장).
- 서버 인증서 검증을 끄지 않는다. 기본 보안 방식은 STARTTLS다. `None`(평문)은 사내 릴레이나 테스트에만 쓴다.
- 메일 본문(확인·재설정 토큰)은 로그와 감사 로그에 남기지 않는다. 실패 로그에는 수신자와 제목만 남는다.

## 운영 전 확인

```text
1. Email:Smtp:* 설정 (Password는 Secret Store)
2. 테스트 계정으로 register → 확인 메일 수신 → confirm-email
3. forgot-password → 재설정 메일 수신 → reset-password
4. SMTP를 일부러 막고 email.failed 감사 로그와 오류 로그 확인
5. SPF/DKIM/DMARC 등 발신 도메인 설정 확인 (스팸 분류 방지)
```

---

**기준 소스:** IdentityTotpSelfServiceApi v4  
**문서 목적:** 개발/테스트/운영 이관 및 후속 회귀 개선 기준
