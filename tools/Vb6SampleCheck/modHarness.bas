Attribute VB_Name = "modHarness"
Option Explicit
' Verifies VB6Sample\modIdentityApi.bas against a live API. Args: base email password totpKey outFile
Private Declare Sub ExitProcess Lib "kernel32" (ByVal uExitCode As Long)
Private mLog As String, mFail As Long

Private Sub Check(ByVal name As String, ByVal cond As Boolean)
    If cond Then
        mLog = mLog & "PASS  " & name & "  [HTTP " & gLastStatus & "]" & vbCrLf
    Else
        mFail = mFail + 1
        mLog = mLog & "FAIL  " & name & "  [HTTP " & gLastStatus & "]" & vbCrLf
    End If
End Sub

Sub Main()
    Dim a() As String, need As Boolean, ch As String, st As Long, s As String, oldRefresh As String, ok As Boolean, f As Integer
    a = Split(Command$, " ")
    ApiInit a(0)

    ok = ApiLogin(a(1), "Wrong!Pass999", need, ch)
    Check "ApiLogin wrong password -> False, 401", (Not ok) And gLastStatus = 401

    ok = ApiLogin(a(1), a(2), need, ch)
    Check "ApiLogin -> True, requires2FA, challenge token", ok And need And Len(ch) > 0 And Len(gAccessToken) = 0

    gAccessToken = ch
    s = ApiGet("/api/account/me", st)
    Check "challenge token used as access token -> 401 (REG-008)", st = 401
    gAccessToken = ""

    ok = ApiTotp(ch, "000000")
    If Totp(a(3)) <> "000000" Then Check "ApiTotp wrong code -> False, 401", (Not ok) And gLastStatus = 401

    ok = ApiTotp(ch, Totp(a(3)))
    Check "ApiTotp -> True, access/refresh saved", ok And Len(gAccessToken) > 0 And Len(gRefreshToken) > 0

    s = ApiGet("/api/account/me", st)
    Check "ApiGet /api/account/me -> 200, email", st = 200 And InStr(1, s, a(1), vbTextCompare) > 0

    oldRefresh = gRefreshToken
    ok = ApiRefresh()
    Check "ApiRefresh -> True, rotated refresh token", ok And Len(gRefreshToken) > 0 And gRefreshToken <> oldRefresh

    s = ApiGet("/api/account/me", st)
    Check "ApiGet with refreshed access token -> 200", st = 200

    ok = ApiLogout()
    Check "ApiLogout -> True (204), tokens cleared", ok And gLastStatus = 204 And Len(gRefreshToken) = 0

    gRefreshToken = oldRefresh
    ok = ApiRefresh()
    Check "ApiRefresh with rotated (old) token -> False, 401", (Not ok) And gLastStatus = 401

    ApiInit "http://localhost:1"
    ok = ApiLogin(a(1), a(2), need, ch)
    Check "ApiLogin unreachable server -> False, status 0", (Not ok) And gLastStatus = 0

    mLog = mLog & "RESULT failures=" & mFail & vbCrLf
    f = FreeFile
    Open a(4) For Output As #f
    Print #f, mLog;
    Close #f
    ExitProcess IIf(mFail = 0, 0, 1)
End Sub
