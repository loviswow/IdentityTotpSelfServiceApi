# 테스트 클라이언트

전체 REST API를 실제 서버(SQL Server)에 대해 호출하는 Web·VB6 테스트 프로그램입니다. 두 클라이언트는 같은 시나리오(회원가입 → 이메일 확인 → 로그인 → 토큰 → TOTP 등록·인증 → 복구 코드 → 비밀번호 변경·재설정 → 2FA 초기화·해제 → 세션(기기) 관리 → 관리자 → 계정 잠금)를 검증합니다.

## 한 번에 실행 (권장)

```powershell
.\scripts\run-e2e.ps1 -SqlServer "localhost\SQLEXPRESS" -Database IdentityTotp_Test
```

스크립트가 하는 일:
1. API를 `.e2e\api`로 publish합니다.
2. 테스트 DB를 만들고 `MigrationsSql\NNN_*.sql`을 적용합니다.
3. API를 Development 환경으로 실행합니다. 이때 `Email:PickupDirectory=.e2e\mail`을 설정합니다.
4. 관리자 계정을 만듭니다(가입 → 메일 확인 → `Admin` 역할 부여 → 2FA 활성화). 관리자 API는 TOTP로 로그인한 세션만 허용하므로, 관리자 TOTP 키를 시나리오에 `email:password:key` 형식으로 넘깁니다.
5. 시나리오를 실행합니다.
   - Web(Node)
   - Web(헤드리스 Chrome)
   - VB6(`/auto`)
6. 감사 로그를 점검합니다.

모두 통과하면 종료 코드 0입니다. `-KeepRunning`을 주면 서버를 띄워 둔 채 끝나므로, 화면으로 직접 테스트할 수 있습니다.

> **주의:** 운영 DB에는 사용하지 마십시오. 테스트 사용자와 감사 로그가 계속 쌓입니다.

## 직접 실행

### API (테스트 메일을 파일로 저장)
```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"; $env:ASPNETCORE_URLS="http://localhost:5080"
$env:ConnectionStrings__DefaultConnection="Server=localhost\SQLEXPRESS;Database=IdentityTotp_Test;Trusted_Connection=True;TrustServerCertificate=True"
$env:Email__PickupDirectory="D:\work\totp\IdentityTotpSelfServiceApi_v4\.e2e\mail"
dotnet run --project IdentityTotpSelfServiceApi
```

`Email:PickupDirectory`는 **Development 환경에서만** 동작합니다. 확인 토큰과 재설정 토큰을 디스크에 남기기 때문입니다.

### Web (`WebTestClient/`)
```powershell
cd tools\WebTestClient
$env:API_URL="http://localhost:5080"; $env:MAIL_DIR="..\..\.e2e\mail"; node server.js   # http://localhost:5090
node run-scenario.mjs --base http://localhost:5090 --admin "admin@e2e.local:Admin!Pass123:<관리자 TOTP 키>"   # Node로 시나리오 실행
node run-browser.mjs  --url  http://localhost:5090 --admin "admin@e2e.local:Admin!Pass123:<관리자 TOTP 키>"   # 헤드리스 Chrome
```

| 파일 | 역할 |
|---|---|
| `server.js` | 외부 패키지가 필요 없는 개발 서버입니다. 정적 파일 제공, `/api` 프록시(같은 출처라 API에 CORS 설정 불필요), 테스트 메일 조회(`/dev/mail`)를 맡습니다. |
| `public/index.html` | API별 버튼, 현재 토큰·TOTP 상태, 전체 시나리오 실행 버튼이 있는 화면입니다. `?autorun=1`로 열면 시나리오를 바로 실행합니다. |
| `public/scenario.js` | 시나리오 본체입니다. 브라우저와 Node가 같은 코드를 씁니다. TOTP는 WebCrypto HMAC-SHA1로 계산합니다. |

### VB6 (`Vb6TestClient/`)
- **화면 모드:** `Vb6TestClient.exe`를 실행합니다. 버튼으로 API를 하나씩 호출하거나 전체 시나리오를 실행합니다.
- **자동 모드:** 아래처럼 실행합니다. 실패가 있으면 종료 코드 1입니다.
  ```
  Vb6TestClient.exe /auto base=http://localhost:5080 mail=<메일폴더> admin=<email>:<pw>:<관리자 TOTP 키> out=result.txt
  ```
- **TOTP 확인:** `Vb6TestClient.exe /totp key=<BASE32> [time=<유닉스초>] out=code.txt`
- **빌드:** `VB6.EXE /make Vb6TestClient.vbp /out build.log`

| 모듈 | 역할 |
|---|---|
| `modHttp` | `WinHttp.WinHttpRequest.5.1`로 호출합니다. 요청 본문은 `ADODB.Stream`으로 UTF-8로 보내므로 한글 사유도 깨지지 않습니다. 로그의 토큰은 가립니다. |
| `modTotp` | Windows CryptoAPI(advapi32)의 HMAC-SHA1로 TOTP를 계산합니다. RFC 6238 테스트 벡터와 일치합니다. 실제 업무 프로그램에서도 재사용할 수 있습니다. |
| `modJson` | 응답 JSON의 최상위 속성을 읽는 최소 파서입니다. 업무용으로는 검증된 파서를 쓰십시오. |

소스 파일은 VB6 IDE가 읽을 수 있도록 **CP949(ANSI) + CRLF**로 저장되어 있습니다. UTF-8로 저장하면 한글 문자열이 깨집니다.

### VB6 샘플 모듈 검증 (`Vb6SampleCheck/`)
업무 프로그램용 샘플 모듈 `IdentityTotpSelfServiceApi/VB6Sample/modIdentityApi.bas`를 **그대로 포함해서** 실제 API에 호출합니다. 2FA 로그인(deviceName 포함), challenge token 오용 거부, 세션 목록의 기기 이름, Refresh 회전, 로그아웃, 연결 실패 처리를 검증합니다. `run-e2e.ps1`이 2FA 사용자를 만들어 자동으로 실행합니다.

수동 실행:
```
SampleCheck.exe <API주소> <이메일> <비밀번호> <TOTP키> <결과파일>
```
대상 사용자는 2FA가 켜져 있어야 합니다.
