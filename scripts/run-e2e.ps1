<#
.SYNOPSIS
  실제 API(SQL Server) + Web 테스트 클라이언트 + VB6 테스트 클라이언트로 전체 API 시나리오를 실행한다.

.DESCRIPTION
  1. API를 .e2e\api 로 publish (bin 폴더를 잠그지 않도록)
  2. 테스트 DB 생성 및 MigrationsSql\NNN_*.sql 적용 (rollback 제외)
  3. API를 Development + Email:PickupDirectory(.e2e\mail)로 실행
  4. Web 테스트 클라이언트 서버(tools\WebTestClient\server.js) 실행
  5. 관리자 계정 생성 (가입 → 메일 확인 → Admin 역할 부여 → 2FA 활성화. 관리자 API는 MFA 세션 필수)
  6. Web 시나리오(Node) / VB6 시나리오(/auto) 실행
  7. 감사 로그 점검 후 서버 종료

  운영 DB에는 절대 사용하지 않는다. 테스트 사용자/감사 로그가 계속 쌓인다.

.EXAMPLE
  .\scripts\run-e2e.ps1 -SqlServer "localhost\SQLEXPRESS" -Database IdentityTotp_Test
#>
param(
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [string]$Database = "IdentityTotp_Test",
    [int]$ApiPort = 5080,
    [int]$WebPort = 5090,
    [string]$Dotnet = "",
    [string]$Vb6 = "C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE",
    [string]$Chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe",
    [switch]$SkipWeb,
    [switch]$SkipVb6,
    [switch]$KeepRunning
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$e2e = Join-Path $root ".e2e"
$mail = Join-Path $e2e "mail"
$webDir = Join-Path $root "tools\WebTestClient"
$vbDir = Join-Path $root "tools\Vb6TestClient"
$cp949 = [Text.Encoding]::GetEncoding(949)

function Step($msg) { Write-Host "`n=== $msg" -ForegroundColor Cyan }

# ---------- 도구 찾기 ----------
if (-not $Dotnet) {
    $local = Join-Path (Split-Path -Parent $root) ".dotnet\dotnet.exe"
    $Dotnet = if (Test-Path $local) { $local } else { "dotnet" }
}
$sqlcmd = (Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) {
    $sqlcmd = Get-ChildItem "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\SQLCMD.EXE" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $sqlcmd) { throw "sqlcmd를 찾을 수 없습니다." }
$conn = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"

# TOTP 코드는 Web 시나리오와 같은 구현(scenario.js의 totp)으로 계산한다.
function Totp([string]$key) {
    $scenario = "file:///" + ((Join-Path $webDir "public\scenario.js") -replace '\\', '/')
    (node --input-type=module -e "import {totp} from '$scenario'; console.log(await totp('$key'))").Trim()
}

function Sql([string]$query) {
    # -I: QUOTED_IDENTIFIER ON (필터 인덱스가 있는 Identity 테이블에 쓰기 위해 필요)
    $out = & $sqlcmd -S $SqlServer -E -C -I -d $Database -b -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "SQL 실패: $query`n$out" }
    $out
}

$procs = @()
$exitCode = 1
try {
    New-Item -ItemType Directory -Force $e2e, $mail | Out-Null
    Get-ChildItem $mail -Filter *.txt -ErrorAction SilentlyContinue | Remove-Item -Force

    Step "API publish"
    & $Dotnet publish (Join-Path $root "IdentityTotpSelfServiceApi\IdentityTotpSelfServiceApi.csproj") -c Release -o (Join-Path $e2e "api") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish 실패" }

    Step "DB 준비: $SqlServer / $Database"
    & $sqlcmd -S $SqlServer -E -C -b -Q "IF DB_ID('$Database') IS NULL CREATE DATABASE [$Database]"
    if ($LASTEXITCODE -ne 0) { throw "DB 생성 실패" }
    Get-ChildItem (Join-Path $root "IdentityTotpSelfServiceApi\MigrationsSql") -Filter "*.sql" |
        Where-Object { $_.Name -match '^\d{3}_' -and $_.Name -notmatch '_rollback\.sql$' } | Sort-Object Name | ForEach-Object {
            Write-Host "  $($_.Name)"
            & $sqlcmd -S $SqlServer -E -C -d $Database -b -i $_.FullName | Where-Object { $_ -notmatch '900' }
            if ($LASTEXITCODE -ne 0) { throw "$($_.Name) 적용 실패" }
        }

    Step "API 실행 (http://localhost:$ApiPort)"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
    $env:ConnectionStrings__DefaultConnection = $conn
    $env:Email__PickupDirectory = $mail
    $env:RateLimiting__AuthPermitLimit = "100000"   # 429는 xUnit(RateLimitTests)에서 검증한다.
    $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $env:Jwt__Key = [Convert]::ToBase64String($bytes)
    $procs += Start-Process $Dotnet -ArgumentList "IdentityTotpSelfServiceApi.dll" -WorkingDirectory (Join-Path $e2e "api") `
        -RedirectStandardOutput (Join-Path $e2e "api.log") -RedirectStandardError (Join-Path $e2e "api.err.log") -PassThru -WindowStyle Hidden
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        try { $ready = (Invoke-WebRequest -UseBasicParsing "http://localhost:$ApiPort/swagger/v1/swagger.json" -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "API가 시작되지 않았습니다. .e2e\api.log 확인" }

    if (-not $SkipWeb) {
        Step "Web 테스트 클라이언트 서버 실행 (http://localhost:$WebPort)"
        $env:API_URL = "http://localhost:$ApiPort"; $env:MAIL_DIR = $mail; $env:PORT = "$WebPort"
        $procs += Start-Process node -ArgumentList "server.js" -WorkingDirectory $webDir `
            -RedirectStandardOutput (Join-Path $e2e "web.log") -RedirectStandardError (Join-Path $e2e "web.err.log") -PassThru -WindowStyle Hidden
        Start-Sleep -Seconds 1
    }

    Step "관리자 계정 준비"
    $adminEmail = "admin-$([guid]::NewGuid().ToString('N').Substring(0,8))@e2e.local"
    $adminPw = "Admin!Pass123"
    $api = "http://localhost:$ApiPort"
    Invoke-RestMethod -Method Post "$api/api/auth/register" -ContentType "application/json" -Body (@{ email = $adminEmail; password = $adminPw } | ConvertTo-Json) | Out-Null
    $m = Get-ChildItem $mail -Filter *.txt | Sort-Object Name -Descending | ForEach-Object { Get-Content $_.FullName -Raw } |
        Where-Object { $_ -match "^To: $([regex]::Escape($adminEmail))" } | Select-Object -First 1
    if ($m -notmatch 'Confirmation token: ([^;\s]+); userId: (\S+)') { throw "관리자 확인 메일 없음" }
    Invoke-RestMethod -Method Post "$api/api/auth/confirm-email" -ContentType "application/json" -Body (@{ userId = $Matches[2]; token = $Matches[1] } | ConvertTo-Json) | Out-Null
    Sql "IF NOT EXISTS(SELECT 1 FROM AspNetRoles WHERE NormalizedName='ADMIN') INSERT AspNetRoles(Id,Name,NormalizedName,ConcurrencyStamp) VALUES(CONVERT(nvarchar(450),NEWID()),'Admin','ADMIN',CONVERT(nvarchar(450),NEWID()));
         INSERT AspNetUserRoles(UserId,RoleId) SELECT u.Id,r.Id FROM AspNetUsers u CROSS JOIN AspNetRoles r WHERE u.NormalizedEmail=UPPER('$adminEmail') AND r.NormalizedName='ADMIN'" | Out-Null
    # 관리자 API는 MFA 세션만 허용하므로 관리자도 2FA를 켠다. TOTP 키는 시나리오에 email:password:key로 넘긴다.
    $at = (Invoke-RestMethod -Method Post "$api/api/auth/login" -ContentType "application/json" -Body (@{ email = $adminEmail; password = $adminPw } | ConvertTo-Json)).accessToken
    $adminKey = (Invoke-RestMethod -Method Post "$api/api/account/2fa/setup" -Headers @{ Authorization = "Bearer $at" }).sharedKey
    Invoke-RestMethod -Method Post "$api/api/account/2fa/enable" -Headers @{ Authorization = "Bearer $at" } -ContentType "application/json" -Body (@{ code = (Totp $adminKey) } | ConvertTo-Json) | Out-Null
    $adminArg = "${adminEmail}:${adminPw}:$adminKey"
    Write-Host "  $adminEmail (Admin, 2FA)"

    $results = [ordered]@{}
    if (-not $SkipWeb) {
        Step "Web 시나리오 (scenario.js, Node 실행)"
        Push-Location $webDir
        try { node run-scenario.mjs --base "http://localhost:$WebPort" --admin $adminArg --out (Join-Path $e2e "web-result.json") } finally { Pop-Location }
        $results["Web(Node)"] = $LASTEXITCODE

        if (Test-Path $Chrome) {
            Step "Web 시나리오 (index.html, 헤드리스 Chrome)"
            Push-Location $webDir
            try { node run-browser.mjs --url "http://localhost:$WebPort" --admin $adminArg --chrome $Chrome --out (Join-Path $e2e "browser-result.txt") } finally { Pop-Location }
            $results["Web(Chrome)"] = $LASTEXITCODE
        } else { Write-Host "Chrome이 없어 브라우저 검증을 건너뜁니다: $Chrome" -ForegroundColor Yellow }
    }

    if (-not $SkipVb6) {
        Step "VB6 시나리오 (Vb6TestClient.exe /auto)"
        $exe = Join-Path $vbDir "Vb6TestClient.exe"
        $newest = Get-ChildItem $vbDir -Include *.bas, *.frm, *.vbp -File -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $newest.LastWriteTime) {
            if (-not (Test-Path $Vb6)) { throw "VB6.EXE가 없어 Vb6TestClient.exe를 빌드할 수 없습니다: $Vb6" }
            $log = Join-Path $vbDir "build.log"; if (Test-Path $log) { Remove-Item $log }
            Start-Process $Vb6 -ArgumentList "/make `"$(Join-Path $vbDir 'Vb6TestClient.vbp')`" /out `"$log`"" -Wait -WindowStyle Hidden
            $b = [IO.File]::ReadAllText($log, $cp949); Write-Host $b.Trim()
            if ($b -notmatch 'succeeded') { throw "VB6 빌드 실패" }
        }
        $out = Join-Path $e2e "vb6-result.txt"; if (Test-Path $out) { Remove-Item $out }
        $p = Start-Process $exe -ArgumentList @("/auto", "base=$api", "mail=$mail", "admin=$adminArg", "out=$out") -Wait -PassThru
        if (Test-Path $out) { [IO.File]::ReadAllText($out, $cp949) | Write-Host } else { Write-Host "결과 파일 없음" -ForegroundColor Red }
        $results["VB6"] = $p.ExitCode

        Step "VB6Sample 모듈 검증 (tools\Vb6SampleCheck: modIdentityApi.bas를 실제 API로 호출)"
        $chkDir = Join-Path $root "tools\Vb6SampleCheck"
        $chkExe = Join-Path $chkDir "SampleCheck.exe"
        $log = Join-Path $chkDir "build.log"; if (Test-Path $log) { Remove-Item $log }
        Start-Process $Vb6 -ArgumentList "/make `"$(Join-Path $chkDir 'SampleCheck.vbp')`" /out `"$log`"" -Wait -WindowStyle Hidden
        if ([IO.File]::ReadAllText($log, $cp949) -notmatch 'succeeded') { throw "SampleCheck 빌드 실패" }
        # 2FA가 켜진 사용자 준비: 가입 → 메일 확인 → 로그인 → setup → enable (TOTP는 scenario.js의 totp로 계산)
        $sEmail = "vb6sample-$([guid]::NewGuid().ToString('N').Substring(0,8))@e2e.local"; $sPw = "Strong!Pass123"
        Invoke-RestMethod -Method Post "$api/api/auth/register" -ContentType "application/json" -Body (@{ email = $sEmail; password = $sPw } | ConvertTo-Json) | Out-Null
        $m = Get-ChildItem $mail -Filter *.txt | Sort-Object Name -Descending | ForEach-Object { Get-Content $_.FullName -Raw } |
            Where-Object { $_ -match "^To: $([regex]::Escape($sEmail))" } | Select-Object -First 1
        if ($m -notmatch 'Confirmation token: ([^;\s]+); userId: (\S+)') { throw "샘플 검증용 확인 메일 없음" }
        Invoke-RestMethod -Method Post "$api/api/auth/confirm-email" -ContentType "application/json" -Body (@{ userId = $Matches[2]; token = $Matches[1] } | ConvertTo-Json) | Out-Null
        $at = (Invoke-RestMethod -Method Post "$api/api/auth/login" -ContentType "application/json" -Body (@{ email = $sEmail; password = $sPw } | ConvertTo-Json)).accessToken
        $key = (Invoke-RestMethod -Method Post "$api/api/account/2fa/setup" -Headers @{ Authorization = "Bearer $at" }).sharedKey
        Invoke-RestMethod -Method Post "$api/api/account/2fa/enable" -Headers @{ Authorization = "Bearer $at" } -ContentType "application/json" -Body (@{ code = (Totp $key) } | ConvertTo-Json) | Out-Null
        $out = Join-Path $e2e "vb6sample-result.txt"; if (Test-Path $out) { Remove-Item $out }
        $p = Start-Process $chkExe -ArgumentList @($api, $sEmail, $sPw, $key, $out) -Wait -PassThru
        if (Test-Path $out) { Get-Content $out | Write-Host } else { Write-Host "결과 파일 없음" -ForegroundColor Red }
        $results["VB6Sample 모듈"] = $p.ExitCode
    }

    Step "감사 로그 점검 (이번 실행)"
    $since = (Get-Date).ToUniversalTime().AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss")
    Sql "SELECT EventType + ' (' + CASE Success WHEN 1 THEN 'ok' ELSE 'fail' END + ') = ' + CAST(COUNT(*) AS varchar) FROM AuditLogs a JOIN AspNetUsers u ON u.Id=a.UserId WHERE u.Email LIKE '%@e2e.local' AND a.OccurredAt >= '$since' GROUP BY EventType, Success ORDER BY EventType" | ForEach-Object { "  $_" }
    $leak = Sql "SELECT COUNT(*) FROM AuditLogs WHERE OccurredAt >= '$since' AND (Detail LIKE '%eyJ%' OR Detail LIKE '%Confirmation token%' OR Detail LIKE '%Reset token%')"
    $results["Audit(토큰 미기록)"] = if ([int]($leak | Select-Object -First 1) -eq 0) { 0 } else { 1 }

    Step "요약"
    $results.GetEnumerator() | ForEach-Object { "  {0,-20} {1}" -f $_.Key, $(if ($_.Value -eq 0) { "통과" } else { "실패" }) }
    $exitCode = if (@($results.Values | Where-Object { $_ -ne 0 }).Count -eq 0) { 0 } else { 1 }
}
finally {
    if ($KeepRunning) {
        Write-Host "`n서버를 계속 실행합니다. API http://localhost:$ApiPort  Web http://localhost:$WebPort  (PID: $($procs.Id -join ', '))"
    } else {
        $procs | Where-Object { $_ -and -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force }
    }
}
exit $exitCode
