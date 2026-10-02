Attribute VB_Name = "modScenario"
Option Explicit

' 전체 API 시나리오. Web 테스트 클라이언트(scenario.js)와 같은 순서·조건을 VB6에서 검증한다.
' TOTP 코드는 modTotp(CryptoAPI)로 VB6가 직접 계산한다.

Private mPass As Long, mFail As Long, mStatus As Long, mBody As String
Private mEmail As String, mPw As String, mKey As String, mUserId As String
Private mAccess As String, mRefresh As String
Private mCodes(0 To 9) As String

Private Function Api(ByVal method As String, ByVal path As String, Optional ByVal body As String = "", Optional ByVal bearer As String = "") As String
    mBody = HttpCall(method, path, body, bearer, mStatus)
    Api = mBody
End Function

Private Function Ok(ByVal name As String, ByVal cond As Boolean, Optional ByVal detail As String = "") As Boolean
    If cond Then
        mPass = mPass + 1
        LogLine "PASS  " & name
    Else
        mFail = mFail + 1
        LogLine "FAIL  " & name & ": " & detail
    End If
    Ok = cond
End Function

Private Function Expect(ByVal name As String, ByVal expected As Long) As Boolean
    Expect = Ok(name, mStatus = expected, "기대 " & expected & ", 실제 " & mStatus & " " & Left$(MaskTokens(mBody), 200))
End Function

Private Function LoginTokens(ByVal name As String, Optional ByVal deviceName As String = "") As Boolean
    If Len(deviceName) > 0 Then
        Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw, "deviceName", deviceName)
    Else
        Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw)
    End If
    If Expect(name, 200) Then
        mAccess = JsonGet(mBody, "accessToken")
        mRefresh = JsonGet(mBody, "refreshToken")
        LoginTokens = Ok(name & " - 토큰 즉시 발급", JsonGet(mBody, "requiresTwoFactor") = "false" And Len(mAccess) > 0 And Len(mRefresh) > 0, MaskTokens(mBody))
    End If
End Function

Private Function Challenge() As String
    Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw)
    If mStatus = 200 And JsonGet(mBody, "requiresTwoFactor") = "true" And JsonGet(mBody, "accessToken") = "null" Then Challenge = JsonGet(mBody, "challengeToken")
End Function

Private Function Login2Fa(ByVal name As String) As Boolean
    Dim ch As String
    ch = Challenge()
    If Not Ok(name & " - challengeToken 발급", Len(ch) > 0, "상태 " & mStatus & " " & MaskTokens(mBody)) Then Exit Function
    Api "POST", "/api/auth/2fa", JsonObj("challengeToken", ch, "code", Totp(mKey))
    If Expect(name & " - TOTP 2차 인증", 200) Then
        mAccess = JsonGet(mBody, "accessToken")
        mRefresh = JsonGet(mBody, "refreshToken")
        Login2Fa = True
    End If
End Function

' 세션 목록(JSON 배열)에서 기기 이름이 deviceName인 세션의 sessionId를 찾는다. 없으면 "".
' 항목은 {"sessionId":"...","deviceName":"...",...} 순서로 오므로 기기 이름 앞의 가장 가까운 sessionId를 읽는다.
Private Function SessionIdOf(ByVal json As String, ByVal deviceName As String) As String
    Dim p As Long, q As Long
    p = InStr(1, json, """deviceName"":""" & deviceName & """", vbBinaryCompare)
    If p = 0 Then Exit Function
    q = InStrRev(json, """sessionId"":""", p, vbBinaryCompare)
    If q > 0 Then SessionIdOf = Between(Mid$(json, q), """sessionId"":""", """")
End Function

' 세션 목록에서 현재 세션("current":true) 항목의 기기 이름. 없으면 "".
Private Function CurrentDevice(ByVal json As String) As String
    Dim p As Long, q As Long
    p = InStr(1, json, """current"":true", vbBinaryCompare)
    If p = 0 Then Exit Function
    q = InStrRev(json, """deviceName"":""", p, vbBinaryCompare)
    If q > 0 Then CurrentDevice = Between(Mid$(json, q), """deviceName"":""", """")
End Function

Private Function WrongCode() As String
    If Totp(mKey) = "000000" Then WrongCode = "111111" Else WrongCode = "000000"
End Function

Private Function EnableTotp(ByVal name As String) As Boolean
    Dim i As Long
    Api "POST", "/api/account/2fa/setup", , mAccess
    If Not Expect(name & " - setup", 200) Then Exit Function
    mKey = JsonGet(mBody, "sharedKey")
    Api "POST", "/api/account/2fa/enable", JsonObj("code", Totp(mKey)), mAccess
    If Expect(name & " - enable (VB6 TOTP 계산)", 200) Then
        For i = 0 To 9
            mCodes(i) = JsonArrayItem(mBody, "recoveryCodes", i)
        Next
        EnableTotp = True
    End If
End Function

' 실패 개수를 돌려준다.
Public Function RunScenario() As Long
    Dim info As String, ch As String, oldAt As String, oldRt As String, tAccess As String, tRefresh As String, adminAt As String, tok As String, npw As String, i As Long

    mPass = 0: mFail = 0
    Randomize
    mEmail = "vb6-" & LCase$(Hex$(Int(Rnd * 2147483647#))) & "@e2e.local"
    mPw = "Strong!Pass123"
    LogLine "테스트 사용자: " & mEmail & "  (API " & gBaseUrl & ", 메일 " & gMailDir & ")"

    ' ---------- 회원가입 / 이메일 확인 ----------
    Api "POST", "/api/auth/register", JsonObj("email", mEmail, "password", mPw)
    Expect "AUTH 회원가입 → 201", 201
    info = LastMailBody(mEmail, "Confirm email")
    mUserId = Between(info, "userId: ", vbCrLf)
    Ok "AUTH 확인 메일 수신", Len(mUserId) > 0, "메일 폴더에 확인 메일이 없습니다"
    Api "POST", "/api/auth/register", JsonObj("email", mEmail, "password", mPw)
    Expect "AUTH 중복 이메일 회원가입 → 400", 400
    Api "POST", "/api/auth/register", JsonObj("email", "weak-" & mEmail, "password", "weak")
    Expect "AUTH 약한 비밀번호 회원가입 → 400", 400
    Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw)
    Expect "AUTH 이메일 미확인 로그인 → 401", 401
    Api "POST", "/api/auth/resend-confirmation", JsonObj("email", mEmail)
    Expect "AUTH 확인 메일 재발송 → 202", 202
    Api "POST", "/api/auth/confirm-email", JsonObj("userId", mUserId, "token", "invalid")
    Expect "AUTH 잘못된 확인 토큰 → 400", 400
    info = LastMailBody(mEmail, "Confirm email")
    Api "POST", "/api/auth/confirm-email", JsonObj("userId", mUserId, "token", Between(info, "Confirmation token: ", ";"))
    Expect "AUTH 이메일 확인 → 204", 204

    ' ---------- 로그인 / 토큰 ----------
    Api "POST", "/api/auth/login", JsonObj("email", "none-" & mEmail, "password", mPw)
    Expect "AUTH 존재하지 않는 사용자 → 401", 401
    Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", "Wrong!Pass999")
    Expect "AUTH 잘못된 비밀번호 → 401", 401
    LoginTokens "AUTH 정상 로그인"
    Api "GET", "/api/account/me", , mAccess
    If Expect("ACCOUNT 내 정보 /me → 200", 200) Then Ok "ACCOUNT /me 이메일 일치", JsonGet(mBody, "email") = mEmail, mBody
    Api "GET", "/api/account/me"
    Expect "ACCOUNT 토큰 없이 /me → 401", 401
    Api "GET", "/api/account/2fa/status", , mAccess
    If Expect("2FA 상태 조회 → 200", 200) Then Ok "2FA 비활성 상태", JsonGet(mBody, "isTwoFactorEnabled") = "false", mBody
    oldRt = mRefresh
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", oldRt)
    If Expect("TOKEN Refresh 회전 → 200", 200) Then
        mAccess = JsonGet(mBody, "accessToken")
        mRefresh = JsonGet(mBody, "refreshToken")
        Ok "TOKEN 새 Refresh Token 발급", Len(mRefresh) > 0 And mRefresh <> oldRt And Len(JsonGet(mBody, "accessTokenExpiresAt")) > 0, MaskTokens(mBody)
    End If
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", oldRt)
    Expect "TOKEN 회전된 Refresh 재사용 → 401", 401
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
    Expect "TOKEN 재사용 탐지 후 최신 Refresh도 폐기 → 401", 401
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", "not-a-token")
    Expect "TOKEN 잘못된 Refresh → 401", 401
    LoginTokens "TOKEN 로그아웃용 로그인"
    Api "POST", "/api/auth/token/revoke", JsonObj("refreshToken", mRefresh)
    Expect "TOKEN 로그아웃(revoke) → 204", 204
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
    Expect "TOKEN 로그아웃 후 Refresh → 401", 401

    ' ---------- TOTP 등록 ----------
    LoginTokens "2FA 등록용 로그인"
    Api "POST", "/api/account/2fa/setup", , mAccess
    If Expect("2FA setup → 200", 200) Then
        mKey = JsonGet(mBody, "sharedKey")
        Ok "2FA sharedKey/otpauth URI", Len(mKey) > 0 And Left$(JsonGet(mBody, "authenticatorUri"), 15) = "otpauth://totp/", mBody
    End If
    Api "POST", "/api/account/2fa/enable", JsonObj("code", WrongCode()), mAccess
    Expect "2FA enable 잘못된 코드 → 400", 400
    oldAt = mAccess: oldRt = mRefresh
    Api "POST", "/api/account/2fa/enable", JsonObj("code", Totp(mKey)), mAccess
    If Expect("2FA enable (VB6 TOTP 계산) → 200", 200) Then
        For i = 0 To 9
            mCodes(i) = JsonArrayItem(mBody, "recoveryCodes", i)
        Next
        Ok "2FA 복구 코드 10개", Len(mCodes(9)) > 0 And Len(JsonArrayItem(mBody, "recoveryCodes", 10)) = 0, mBody
    End If
    Api "GET", "/api/account/me", , oldAt
    Expect "2FA enable 후 기존 Access → 401 (SecurityStamp)", 401
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", oldRt)
    Expect "2FA enable 후 기존 Refresh → 401 (REG-007)", 401
    ch = Challenge()
    Ok "2FA 로그인 → challengeToken만 발급", Len(ch) > 0, MaskTokens(mBody)
    Api "POST", "/api/auth/2fa", JsonObj("challengeToken", ch, "code", WrongCode())
    Expect "2FA 잘못된 TOTP → 401", 401
    Api "POST", "/api/auth/2fa", JsonObj("challengeToken", "forged.token.value", "code", Totp(mKey))
    Expect "2FA 위조 challengeToken → 401", 401
    Api "POST", "/api/auth/2fa", JsonObj("challengeToken", ch, "code", Totp(mKey))
    If Expect("2FA 정상 TOTP → 200", 200) Then mAccess = JsonGet(mBody, "accessToken"): mRefresh = JsonGet(mBody, "refreshToken")
    Api "GET", "/api/account/me", , Challenge()
    Expect "2FA challengeToken을 Access Token으로 사용 → 401 (REG-008)", 401
    Api "GET", "/api/account/2fa/status", , mAccess
    If Expect("2FA 상태 조회 → 200", 200) Then Ok "2FA 활성, 복구 코드 10개", JsonGet(mBody, "isTwoFactorEnabled") = "true" And JsonGet(mBody, "recoveryCodesLeft") = "10", mBody
    Api "POST", "/api/account/2fa/setup", , mAccess
    Expect "2FA 활성 상태에서 setup → 409", 409
    Api "POST", "/api/account/2fa/recovery-codes/regenerate", JsonObj("code", WrongCode()), mAccess
    Expect "2FA 복구 코드 재발급 잘못된 코드 → 401", 401
    tok = mCodes(0)
    Api "POST", "/api/account/2fa/recovery-codes/regenerate", JsonObj("code", Totp(mKey)), mAccess
    If Expect("2FA 복구 코드 재발급 → 200", 200) Then
        For i = 0 To 9
            mCodes(i) = JsonArrayItem(mBody, "recoveryCodes", i)
        Next
    End If
    Api "POST", "/api/auth/2fa/recovery", JsonObj("challengeToken", Challenge(), "recoveryCode", tok)
    Expect "2FA 재발급 전 복구 코드 → 401", 401
    Api "POST", "/api/auth/2fa/recovery", JsonObj("challengeToken", Challenge(), "recoveryCode", mCodes(0))
    Expect "2FA 복구 코드 로그인 → 200", 200
    Api "POST", "/api/auth/2fa/recovery", JsonObj("challengeToken", Challenge(), "recoveryCode", mCodes(0))
    Expect "2FA 같은 복구 코드 재사용 → 401", 401

    ' ---------- 비밀번호 변경 ----------
    If Login2Fa("PWD 비밀번호 변경용 로그인") Then
        npw = "Changed!Pass456"
        Api "POST", "/api/account/change-password", JsonObj("currentPassword", "Wrong!Pass999", "newPassword", npw), mAccess
        Expect "PWD 현재 비밀번호 오류 → 400", 400
        Api "POST", "/api/account/change-password", JsonObj("currentPassword", mPw, "newPassword", npw), mAccess
        Expect "PWD 비밀번호 변경 → 204", 204
        Api "GET", "/api/account/me", , mAccess
        Expect "PWD 변경 후 기존 Access → 401", 401
        Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
        Expect "PWD 변경 후 기존 Refresh → 401 (REG-004)", 401
        Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw)
        Expect "PWD 기존 비밀번호 로그인 → 401", 401
        mPw = npw
    End If

    ' ---------- 2FA 초기화 / 비활성화 ----------
    If Login2Fa("2FA reset용 로그인") Then
        Api "POST", "/api/account/2fa/reset", JsonObj("password", "Wrong!Pass999", "code", Totp(mKey)), mAccess
        Expect "2FA reset 잘못된 비밀번호 → 401", 401
        Api "POST", "/api/account/2fa/reset", JsonObj("password", mPw, "code", Totp(mKey)), mAccess
        Expect "2FA reset → 200", 200
        Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
        Expect "2FA reset 후 기존 Refresh → 401 (REG-005)", 401
    End If
    If LoginTokens("2FA reset 후 로그인(2FA 해제됨)") Then
        If EnableTotp("2FA 재등록") Then
            If Login2Fa("2FA disable용 로그인") Then
                Api "POST", "/api/account/2fa/disable", JsonObj("password", mPw, "code", WrongCode()), mAccess
                Expect "2FA disable 잘못된 코드 → 401", 401
                Api "POST", "/api/account/2fa/disable", JsonObj("password", mPw, "code", Totp(mKey)), mAccess
                Expect "2FA disable → 204", 204
                Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
                Expect "2FA disable 후 기존 Refresh → 401 (REG-005)", 401
            End If
        End If
    End If
    If LoginTokens("2FA disable 후 로그인") Then
        Api "POST", "/api/account/2fa/disable", JsonObj("password", mPw, "code", "123456"), mAccess
        Expect "2FA 비활성 상태에서 disable → 400", 400
    End If

    ' ---------- 비밀번호 분실 / 재설정 ----------
    Api "POST", "/api/auth/forgot-password", JsonObj("email", "none-" & mEmail)
    Expect "PWD forgot-password 없는 이메일도 202", 202
    Api "POST", "/api/auth/reset-password", JsonObj("email", mEmail, "token", "invalid", "newPassword", "Reset!Pass789")
    Expect "PWD 잘못된 재설정 토큰 → 400", 400
    tAccess = mAccess: tRefresh = mRefresh
    Api "POST", "/api/auth/forgot-password", JsonObj("email", mEmail)
    Expect "PWD forgot-password → 202", 202
    tok = Between(LastMailBody(mEmail, "Password reset"), "Reset token: ", vbCrLf)
    Ok "PWD 재설정 메일 수신", Len(tok) > 0, "메일 폴더에 재설정 메일이 없습니다"
    Api "POST", "/api/auth/reset-password", JsonObj("email", mEmail, "token", tok, "newPassword", "Reset!Pass789")
    If Expect("PWD reset-password → 204", 204) Then mPw = "Reset!Pass789"
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", tRefresh)
    Expect "PWD 재설정 후 기존 Refresh → 401", 401
    Api "GET", "/api/account/me", , tAccess
    Expect "PWD 재설정 후 기존 Access → 401", 401
    LoginTokens "PWD 새 비밀번호 로그인"

    ' ---------- 세션(기기) 관리 ----------
    Dim pcAt As String, pcRt As String, phAt As String, phRt As String, sid As String
    If LoginTokens("SESSION PC 로그인", "VB6-PC") Then pcAt = mAccess: pcRt = mRefresh
    If LoginTokens("SESSION Phone 로그인", "VB6-Phone") Then phAt = mAccess: phRt = mRefresh
    Api "GET", "/api/account/sessions", , pcAt
    If Expect("SESSION 세션 목록 → 200", 200) Then
        sid = SessionIdOf(mBody, "VB6-Phone")
        Ok "SESSION 두 기기 표시", Len(SessionIdOf(mBody, "VB6-PC")) > 0 And Len(sid) > 0, mBody
        Ok "SESSION 현재 세션 = VB6-PC", CurrentDevice(mBody) = "VB6-PC", mBody
        Ok "SESSION 목록에 Refresh Token 없음", InStr(1, mBody, pcRt, vbBinaryCompare) = 0, "Refresh Token 노출"
    End If
    Api "DELETE", "/api/account/sessions/" & sid, , pcAt
    Expect "SESSION 특정 기기 로그아웃 → 204", 204
    Api "GET", "/api/account/me", , phAt
    Expect "SESSION 로그아웃한 기기 Access → 401", 401
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", phRt)
    Expect "SESSION 로그아웃한 기기 Refresh → 401", 401
    Api "GET", "/api/account/me", , pcAt
    Expect "SESSION 현재 기기 Access 유지 → 200", 200
    Api "DELETE", "/api/account/sessions/no-such-session", , pcAt
    Expect "SESSION 없는 세션 → 404", 404
    If LoginTokens("SESSION Tablet 로그인", "VB6-Tablet") Then tAccess = mAccess: tRefresh = mRefresh
    Api "POST", "/api/account/sessions/revoke-others", , pcAt
    If Expect("SESSION 다른 기기 모두 로그아웃 → 200", 200) Then Ok "SESSION revokedSessions >= 1", Val(JsonGet(mBody, "revokedSessions")) >= 1, mBody
    Api "GET", "/api/account/me", , tAccess
    Expect "SESSION 다른 기기 Access → 401", 401
    Api "POST", "/api/auth/token/revoke", JsonObj("refreshToken", pcRt)
    Expect "SESSION 현재 기기 로그아웃 → 204", 204
    Api "GET", "/api/account/me", , pcAt
    Expect "SESSION 로그아웃한 세션의 Access → 401", 401
    If LoginTokens("SESSION 전체 로그아웃용 로그인 A", "VB6-A") Then tAccess = mAccess: tRefresh = mRefresh
    LoginTokens "SESSION 전체 로그아웃용 로그인 B", "VB6-B"
    Api "POST", "/api/account/sessions/revoke-all", , tAccess
    Expect "SESSION 전체 기기 로그아웃 → 204", 204
    Api "GET", "/api/account/me", , tAccess
    Expect "SESSION 전체 로그아웃 후 A Access → 401", 401
    Api "GET", "/api/account/me", , mAccess
    Expect "SESSION 전체 로그아웃 후 B Access → 401", 401
    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh)
    Expect "SESSION 전체 로그아웃 후 B Refresh → 401", 401
    LoginTokens "SESSION 전체 로그아웃 후 다시 로그인"

    ' ---------- 관리자 (Admin 역할 + MFA 세션 필수) ----------
    Api "POST", "/api/admin/users/2fa/reset", JsonObj("userId", mUserId, "reason", "test"), mAccess
    Expect "ADMIN 일반 사용자 호출 → 403", 403
    If Len(gAdminEmail) > 0 And Len(gAdminKey) > 0 Then
        Api "POST", "/api/auth/login", JsonObj("email", gAdminEmail, "password", gAdminPassword, "deviceName", "VB6-Admin")
        If Expect("ADMIN 관리자 로그인 → 200", 200) Then
            ch = JsonGet(mBody, "challengeToken")
            Ok "ADMIN 관리자는 2FA 사용(challengeToken)", JsonGet(mBody, "requiresTwoFactor") = "true" And Len(ch) > 0, MaskTokens(mBody)
            Api "POST", "/api/auth/2fa", JsonObj("challengeToken", ch, "code", Totp(gAdminKey), "deviceName", "VB6-Admin")
            If Expect("ADMIN 관리자 TOTP 인증 → 200", 200) Then adminAt = JsonGet(mBody, "accessToken")
        End If
        If Len(adminAt) > 0 Then
            If EnableTotp("ADMIN 대상 2FA 등록") Then
                If Login2Fa("ADMIN 대상 사용자 로그인") Then
                    tAccess = mAccess: tRefresh = mRefresh
                    Api "POST", "/api/admin/users/2fa/reset", JsonObj("userId", mUserId, "reason", ""), adminAt
                    Expect "ADMIN 사유 없음 → 400", 400
                    Api "POST", "/api/admin/users/2fa/reset", JsonObj("userId", "no-such-user", "reason", "test"), adminAt
                    Expect "ADMIN 없는 사용자 → 404", 404
                    Api "POST", "/api/admin/users/2fa/reset", JsonObj("userId", mUserId, "reason", "VB6 E2E 기기 분실"), adminAt
                    Expect "ADMIN 2FA 초기화 (한글 사유) → 204", 204
                    Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", tRefresh)
                    Expect "ADMIN 초기화 후 대상 Refresh → 401", 401
                    Api "GET", "/api/account/me", , tAccess
                    Expect "ADMIN 초기화 후 대상 Access → 401", 401
                    LoginTokens "ADMIN 초기화 후 대상 로그인(2FA 해제됨)"
                End If
            End If
            Api "GET", "/api/admin/audit-logs?eventType=admin.2fa.reset&limit=5&userId=" & mUserId, , adminAt
            If Expect("ADMIN 감사 로그 조회 → 200", 200) Then Ok "ADMIN 감사 로그에 사유와 수행자(한글은 \uXXXX로 이스케이프됨)", InStr(1, mBody, """detail"":""VB6 E2E ", vbBinaryCompare) > 0 And InStr(1, mBody, """actorUserId"":""", vbBinaryCompare) > 0, mBody
            Api "GET", "/api/admin/audit-logs?limit=1000", , adminAt
            Expect "ADMIN 감사 로그 limit 초과 → 400", 400
            If LoginTokens("ADMIN 대상 세션 준비", "VB6-Target") Then tAccess = mAccess: tRefresh = mRefresh
            Api "GET", "/api/admin/users/" & mUserId & "/sessions", , adminAt
            If Expect("ADMIN 대상 세션 조회 → 200", 200) Then Ok "ADMIN 대상 세션 표시", Len(SessionIdOf(mBody, "VB6-Target")) > 0, mBody
            Api "POST", "/api/admin/users/sessions/revoke-all", JsonObj("userId", mUserId, "reason", "VB6 E2E 침해 대응"), adminAt
            Expect "ADMIN 대상 전체 로그아웃 → 204", 204
            Api "GET", "/api/account/me", , tAccess
            Expect "ADMIN 전체 로그아웃 후 대상 Access → 401", 401
            Api "POST", "/api/auth/token/refresh", JsonObj("refreshToken", tRefresh)
            Expect "ADMIN 전체 로그아웃 후 대상 Refresh → 401", 401
            LoginTokens "ADMIN 전체 로그아웃 후 대상 로그인"
        End If
    Else
        LogLine "SKIP  ADMIN 관리자 API: 관리자 계정(email:password:totpKey)이 주어지지 않음"
    End If

    ' ---------- 계정 잠금 (마지막: 계정이 잠긴다) ----------
    For i = 1 To 5
        Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", "Wrong!Pass999")
    Next
    Api "POST", "/api/auth/login", JsonObj("email", mEmail, "password", mPw)
    Expect "AUTH 비밀번호 5회 실패 → 정상 비밀번호도 423", 423

    LogLine ""
    LogLine "결과: " & mPass & "/" & (mPass + mFail) & " 통과" & IIf(mFail > 0, ", 실패 " & mFail, "")
    RunScenario = mFail
End Function
