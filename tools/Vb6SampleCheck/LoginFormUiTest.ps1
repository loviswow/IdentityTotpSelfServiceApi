<#
.SYNOPSIS
  VB6Sample 2단계 로그인 화면(frmLogin, IdentityApiSample.exe)을 실제로 조작해 검증한다.
.DESCRIPTION
  2FA 사용자를 API로 만든 뒤 화면에 입력·클릭하고, 단계 전환(창 제목)과 서버 감사 로그로 결과를 확인한다.
  VB6 Label은 창 핸들이 없어 밖에서 읽을 수 없으므로 안내 문구 자체는 확인하지 않는다.
  필요: 실행 중인 API(5080, Development + Email:PickupDirectory=.e2e\mail), 로컬 SQL Server(감사 로그 조회), Node(TOTP 계산),
        빌드된 IdentityApiSample.exe, 데스크톱 세션(화면 조작). 서버는 .\scripts\run-e2e.ps1 -ServeOnly로 띄운다.
.EXAMPLE
  powershell -STA -File .\tools\Vb6SampleCheck\LoginFormUiTest.ps1
#>
param([string]$Exe = "D:\work\totp\IdentityTotpSelfServiceApi_v4\IdentityTotpSelfServiceApi\VB6Sample\IdentityApiSample.exe")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W32 {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
$api = "http://localhost:5080"; $mail = "D:\work\totp\IdentityTotpSelfServiceApi_v4\.e2e\mail"; $pw = "Strong!Pass123"
$sq = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
$scn = "file:///D:/work/totp/IdentityTotpSelfServiceApi_v4/tools/WebTestClient/public/scenario.js"
function Totp($key) { (node --input-type=module -e "import {totp} from '$scn'; console.log(await totp('$key'))").Trim() }
$results = New-Object System.Collections.ArrayList
function Check($name, $cond, $detail = "") { [void]$results.Add([pscustomobject]@{ Result = $(if ($cond) { "PASS" } else { "FAIL" }); Step = $name; Detail = $detail }) }

# ---- 2FA 사용자 준비 (가입 → 확인 → setup → enable) ----
$email = "vb6login-$([guid]::NewGuid().ToString('N').Substring(0,6))@e2e.local"
Invoke-RestMethod -Method Post "$api/api/auth/register" -ContentType application/json -Body (@{ email = $email; password = $pw } | ConvertTo-Json) | Out-Null
Start-Sleep -Milliseconds 300
$m = Get-ChildItem $mail -Filter *.txt | Sort-Object Name -Descending | % { Get-Content $_.FullName -Raw } | ? { $_ -match "^To: $([regex]::Escape($email))" } | Select-Object -First 1
$null = $m -match 'Confirmation token: ([^;\s]+); userId: (\S+)'; $userId = $Matches[2]
Invoke-RestMethod -Method Post "$api/api/auth/confirm-email" -ContentType application/json -Body (@{ userId = $userId; token = $Matches[1] } | ConvertTo-Json) | Out-Null
$at = (Invoke-RestMethod -Method Post "$api/api/auth/login" -ContentType application/json -Body (@{ email = $email; password = $pw } | ConvertTo-Json)).accessToken
$key = (Invoke-RestMethod -Method Post "$api/api/account/2fa/setup" -Headers @{ Authorization = "Bearer $at" }).sharedKey
$codes = (Invoke-RestMethod -Method Post "$api/api/account/2fa/enable" -Headers @{ Authorization = "Bearer $at" } -ContentType application/json -Body (@{ code = (Totp $key) } | ConvertTo-Json)).recoveryCodes
function Audits { (& $sq -S "localhost\SQLEXPRESS" -E -C -d IdentityTotp_Test -h -1 -W -Q "SET NOCOUNT ON; SELECT EventType FROM AuditLogs WHERE UserId='$userId' ORDER BY Id") | ? { $_ } }
function AuditCount($type) { @(Audits | ? { $_ -eq $type }).Count }

# ---- 화면 조작 도우미 ----
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
function Win($proc, $title, $timeoutMs = 8000) {
    $until = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $until) {
        $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
        $w = $AE::RootElement.FindAll($TS::Children, $c) | ? { $_.Current.Name -eq $title } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Ctl($w, $class) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, $class)
    @($w.FindAll($TS::Descendants, $c) | ? { -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 0 } | Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left })
}
function Edits($w) { Ctl $w "ThunderRT6TextBox" }
function SetText($el, $text) { [void][W32]::SendMessage([IntPtr]$el.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $text) }   # WM_SETTEXT (Change 이벤트 발생)
function Click($w, $name) { $b = Ctl $w "ThunderRT6CommandButton" | ? { $_.Current.Name -eq $name } | Select-Object -First 1; if (-not $b) { throw "버튼 없음: $name" }; [void][W32]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }   # BM_CLICK
function Title($proc) { $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id); ($AE::RootElement.FindFirst($TS::Children, $c)).Current.Name }

$proc = Start-Process $Exe -PassThru
try {
    # ---- 1단계 ----
    $w = Win $proc "로그인 - 1단계"; Check "시작 → 1단계 화면" ($null -ne $w) (Title $proc)
    $e = Edits $w; Check "1단계 입력칸 3개(API 주소/이메일/비밀번호)" ($e.Count -eq 3) "edits=$($e.Count)"
    Click $w "다음 >"; Start-Sleep -Milliseconds 700
    Check "빈 입력으로 [다음] → 1단계 유지(서버 호출 없음)" ((Title $proc) -eq "로그인 - 1단계" -and (AuditCount "login.failed") -eq 0) (Title $proc)
    SetText $e[0] $api; SetText $e[1] $email; SetText $e[2] "Wrong!Pass999"
    Click $w "다음 >"; Start-Sleep -Milliseconds 1500
    Check "잘못된 비밀번호 → 1단계 유지, login.failed 기록" ((Title $proc) -eq "로그인 - 1단계" -and (AuditCount "login.failed") -eq 1) (Title $proc)
    SetText $e[2] $pw; Click $w "다음 >"
    # ---- 2단계 ----
    $w2 = Win $proc "로그인 - 2단계 인증"; Check "올바른 비밀번호 → 2단계 화면, login.2fa-required 기록" (($null -ne $w2) -and (AuditCount "login.2fa-required") -eq 1) (Title $proc)
    $otp = (Edits $w2)[0]; Check "2단계 입력칸 1개(OTP)" ((Edits $w2).Count -eq 1)
    $good = Totp $key; $wrong = if ($good -eq "000000") { "111111" } else { "000000" }
    SetText $otp $wrong; Start-Sleep -Milliseconds 1500   # 6자리 → 자동 제출
    Check "틀린 OTP 6자리 → 자동 제출, 2단계 유지, 2fa.failed 기록" ((Title $proc) -eq "로그인 - 2단계 인증" -and (AuditCount "2fa.failed") -eq 1) (Title $proc)
    Check "틀린 OTP 뒤 입력칸 비움" ((Edits $w2)[0].Current.Name -eq "") "값='$((Edits $w2)[0].Current.Name)'"
    SetText $otp "123"; Start-Sleep -Milliseconds 700
    Check "6자리 미만은 제출 안 함" ((AuditCount "2fa.failed") -eq 1 -and (Title $proc) -eq "로그인 - 2단계 인증")
    SetText $otp (Totp $key); Start-Sleep -Milliseconds 2000
    # ---- 로그인 완료 ----
    $w3 = Win $proc "로그인됨"; Check "올바른 OTP → 자동 제출, 로그인 완료, 2fa.success 기록" (($null -ne $w3) -and (AuditCount "2fa.success") -eq 1) (Title $proc)
    $sAt = (Invoke-RestMethod -Method Post "$api/api/auth/2fa" -ContentType application/json -Body (@{ challengeToken = (Invoke-RestMethod -Method Post "$api/api/auth/login" -ContentType application/json -Body (@{ email = $email; password = $pw } | ConvertTo-Json)).challengeToken; code = (Totp $key) } | ConvertTo-Json)).accessToken
    $sess = Invoke-RestMethod "$api/api/account/sessions" -Headers @{ Authorization = "Bearer $sAt" }
    Check "세션 목록에 'VB6 샘플 <PC이름>' 기기 이름" (@($sess | ? { $_.deviceName -like "VB6 샘플 *" }).Count -ge 1) (($sess | % deviceName) -join ", ")
    # ---- 로그아웃 → 1단계 ----
    Click $w3 "로그아웃"
    $w = Win $proc "로그인 - 1단계"; Check "로그아웃 → 1단계, logout 기록" (($null -ne $w) -and (AuditCount "logout") -ge 1) (Title $proc)
    $e = Edits $w; Check "1단계로 돌아오면 비밀번호 칸이 비어 있음(로그인 성공 때 지움)" ($e[2].Current.Name -eq "") "값 길이=$($e[2].Current.Name.Length)"
    Check "이메일은 유지" ($e[1].Current.Name -eq $email)
    # ---- 복구 코드 ----
    SetText $e[2] $pw; Click $w "다음 >"; $w2 = Win $proc "로그인 - 2단계 인증"
    Click $w2 "복구 코드 사용"; Start-Sleep -Milliseconds 500
    Check "[복구 코드 사용] 버튼이 [Google OTP 사용]으로 바뀜" (@(Ctl $w2 "ThunderRT6CommandButton" | ? { $_.Current.Name -eq "Google OTP 사용" }).Count -eq 1)
    SetText (Edits $w2)[0] "wrong-code"; Click $w2 "인증"; Start-Sleep -Milliseconds 1500
    Check "틀린 복구 코드 → 2단계 유지, recovery.failed 기록" ((Title $proc) -eq "로그인 - 2단계 인증" -and (AuditCount "recovery.failed") -eq 1) (Title $proc)
    SetText (Edits $w2)[0] $codes[0]; Click $w2 "인증"
    $w3 = Win $proc "로그인됨"; Check "올바른 복구 코드 → 로그인 완료, recovery.success 기록" (($null -ne $w3) -and (AuditCount "recovery.success") -eq 1) (Title $proc)
    # ---- 처음으로 ----
    Click $w3 "로그아웃"; $w = Win $proc "로그인 - 1단계"; $e = Edits $w; SetText $e[2] $pw; Click $w "다음 >"
    $w2 = Win $proc "로그인 - 2단계 인증"; Click $w2 "< 처음으로"
    Check "[처음으로] → 1단계" ($null -ne (Win $proc "로그인 - 1단계"))
    # ---- 창 닫기 → 세션 폐기 ----
    $e = Edits (Win $proc "로그인 - 1단계"); SetText $e[2] $pw; Click (Win $proc "로그인 - 1단계") "다음 >"; $w2 = Win $proc "로그인 - 2단계 인증"
    SetText (Edits $w2)[0] (Totp $key); $null = Win $proc "로그인됨"; $before = AuditCount "logout"
    $proc.CloseMainWindow() | Out-Null; $proc.WaitForExit(5000) | Out-Null
    Check "로그인 상태로 창 닫기 → 서버 세션 폐기(logout 기록)" ((AuditCount "logout") -eq $before + 1) "logout $before → $(AuditCount 'logout')"
}
catch { Check "예외" $false $_.Exception.Message }
finally { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force } }
$results | Format-Table -AutoSize -Wrap | Out-String -Width 220
"결과: $(@($results | ? Result -eq 'PASS').Count)/$($results.Count) 통과"

if (@($results | ? Result -eq 'FAIL').Count -gt 0) { exit 1 } else { exit 0 }
