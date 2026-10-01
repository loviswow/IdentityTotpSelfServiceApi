VERSION 5.00
Begin VB.Form frmMain
   Caption         =   "Identity TOTP API 테스트 클라이언트 (VB6)"
   ClientHeight    =   9000
   ClientLeft      =   60
   ClientTop       =   450
   ClientWidth     =   12240
   BeginProperty Font
      Name            =   "맑은 고딕"
      Size            =   9
      Charset         =   129
      Weight          =   400
      Underline       =   0   'False
      Italic          =   0   'False
      Strikethrough   =   0   'False
   EndProperty
   LinkTopic       =   "Form1"
   ScaleHeight     =   9000
   ScaleWidth      =   12240
   StartUpPosition =   2  'CenterScreen
   Begin VB.TextBox txtLog
      BeginProperty Font
         Name            =   "굴림체"
         Size            =   9
         Charset         =   129
         Weight          =   400
         Underline       =   0   'False
         Italic          =   0   'False
         Strikethrough   =   0   'False
      EndProperty
      Height          =   3000
      Left            =   120
      Locked          =   -1  'True
      MultiLine       =   -1  'True
      ScrollBars      =   3  'Both
      TabIndex        =   3
      Top             =   5400
      Width           =   12000
   End
   Begin VB.CommandButton cmdApi
      Caption         =   "cmd"
      Height          =   400
      Index           =   0
      Left            =   120
      TabIndex        =   2
      Top             =   2000
      Width           =   1900
   End
   Begin VB.TextBox txtF
      Height          =   330
      Index           =   0
      Left            =   1860
      TabIndex        =   1
      Top             =   100
      Width           =   4000
   End
   Begin VB.Label lblF
      Caption         =   "lbl"
      Height          =   300
      Index           =   0
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   1700
   End
End
Attribute VB_Name = "frmMain"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' 입력칸 번호
Private Const F_BASE As Integer = 0
Private Const F_MAIL As Integer = 1
Private Const F_EMAIL As Integer = 2
Private Const F_PW As Integer = 3
Private Const F_NEWPW As Integer = 4
Private Const F_CODE As Integer = 5
Private Const F_TARGET As Integer = 6
Private Const F_REASON As Integer = 7

' 현재 세션 상태. 토큰은 화면/로그에 앞 8자만 표시한다.
Private mAccess As String
Private mRefresh As String
Private mChallenge As String
Private mKey As String
Private mTop As Long

Private Sub Form_Load()
    Dim labels As Variant, caps As Variant, i As Integer
    labels = Array("API 주소", "메일 폴더", "이메일", "비밀번호", "새 비밀번호", "TOTP/복구 코드", "관리자: 대상 UserId", "관리자: 사유")
    For i = 0 To 7
        If i > 0 Then
            Load lblF(i)
            Load txtF(i)
        End If
        lblF(i).Caption = labels(i)
        lblF(i).Move 120 + (i Mod 2) * 6060, 140 + (i \ 2) * 430, 1700, 300
        txtF(i).Move 1860 + (i Mod 2) * 6060, 100 + (i \ 2) * 430, 4200, 340
        txtF(i).Text = ""
        lblF(i).Visible = True
        txtF(i).Visible = True
    Next
    txtF(F_BASE).Text = gBaseUrl
    txtF(F_MAIL).Text = gMailDir
    txtF(F_EMAIL).Text = "vb6-ui-" & Format$(Now, "hhnnss") & "@e2e.local"
    txtF(F_PW).Text = "Strong!Pass123"
    txtF(F_NEWPW).Text = "Changed!Pass456"
    txtF(F_REASON).Text = "기기 분실"

    caps = Array("회원가입", "메일로 이메일 확인", "확인 메일 재발송", "로그인", "TOTP 인증", "복구 코드 로그인", _
                 "Refresh", "로그아웃", "내 정보", "2FA 상태", "2FA Setup", "2FA Enable", _
                 "2FA Disable", "2FA Reset", "복구 코드 재발급", "비밀번호 변경", "비밀번호 찾기", "비밀번호 재설정(메일)", _
                 "관리자 2FA 초기화", "현재 TOTP 계산", "전체 시나리오 실행", "로그 지우기")
    mTop = 100 + 4 * 430 + 120
    For i = 0 To UBound(caps)
        If i > 0 Then Load cmdApi(i)
        cmdApi(i).Caption = caps(i)
        cmdApi(i).Move 120 + (i Mod 6) * 2010, mTop + (i \ 6) * 460, 1950, 420
        cmdApi(i).Visible = True
    Next
    mTop = mTop + ((UBound(caps) \ 6) + 1) * 460 + 80
    AppendLog "API별 버튼으로 하나씩 호출하거나 [전체 시나리오 실행]으로 전체 흐름을 검증합니다."
    AppendLog "메일 폴더는 API를 Development + Email:PickupDirectory로 실행했을 때 확인/재설정 메일이 저장되는 폴더입니다."
End Sub

Private Sub Form_Resize()
    If Me.WindowState = vbMinimized Then Exit Sub
    If Me.ScaleHeight - mTop - 120 > 600 Then txtLog.Move 120, mTop, Me.ScaleWidth - 240, Me.ScaleHeight - mTop - 120
End Sub

Public Sub AppendLog(ByVal s As String)
    If Len(txtLog.Text) > 60000 Then txtLog.Text = Right$(txtLog.Text, 30000)
    txtLog.SelStart = Len(txtLog.Text)
    txtLog.SelText = s & vbCrLf
End Sub

Private Function Code() As String
    ' 입력칸이 비어 있으면 Setup에서 받은 키로 TOTP를 계산한다.
    Code = Trim$(txtF(F_CODE).Text)
    If Len(Code) = 0 And Len(mKey) > 0 Then Code = Totp(mKey)
End Function

Private Sub SaveTokens(ByVal body As String)
    If Len(JsonGet(body, "accessToken")) > 0 And JsonGet(body, "accessToken") <> "null" Then mAccess = JsonGet(body, "accessToken")
    If Len(JsonGet(body, "refreshToken")) > 0 Then mRefresh = JsonGet(body, "refreshToken")
    If Len(JsonGet(body, "challengeToken")) > 0 And JsonGet(body, "challengeToken") <> "null" Then mChallenge = JsonGet(body, "challengeToken")
End Sub

Private Sub cmdApi_Click(Index As Integer)
    Dim st As Long, body As String, info As String, e As String, ok As Boolean
    gBaseUrl = Trim$(txtF(F_BASE).Text)
    If Right$(gBaseUrl, 1) = "/" Then gBaseUrl = Left$(gBaseUrl, Len(gBaseUrl) - 1)
    gMailDir = Trim$(txtF(F_MAIL).Text)
    e = Trim$(txtF(F_EMAIL).Text)
    Screen.MousePointer = vbHourglass
    On Error GoTo Fail
    ok = True
    Select Case Index
    Case 0
        body = HttpCall("POST", "/api/auth/register", JsonObj("email", e, "password", txtF(F_PW).Text), "", st)
    Case 1
        info = LastMailBody(e, "Confirm email")
        If Len(info) = 0 Then AppendLog "확인 메일을 찾지 못했습니다: " & e: ok = False
        If ok Then body = HttpCall("POST", "/api/auth/confirm-email", JsonObj("userId", Between(info, "userId: ", vbCrLf), "token", Between(info, "Confirmation token: ", ";")), "", st)
    Case 2
        body = HttpCall("POST", "/api/auth/resend-confirmation", JsonObj("email", e), "", st)
    Case 3
        body = HttpCall("POST", "/api/auth/login", JsonObj("email", e, "password", txtF(F_PW).Text), "", st)
    Case 4
        body = HttpCall("POST", "/api/auth/2fa", JsonObj("challengeToken", mChallenge, "code", Code()), "", st)
    Case 5
        body = HttpCall("POST", "/api/auth/2fa/recovery", JsonObj("challengeToken", mChallenge, "recoveryCode", Trim$(txtF(F_CODE).Text)), "", st)
    Case 6
        body = HttpCall("POST", "/api/auth/token/refresh", JsonObj("refreshToken", mRefresh), "", st)
    Case 7
        body = HttpCall("POST", "/api/auth/token/revoke", JsonObj("refreshToken", mRefresh), "", st)
        If st = 204 Then mAccess = "": mRefresh = ""
    Case 8
        body = HttpCall("GET", "/api/account/me", "", mAccess, st)
        If st = 200 Then txtF(F_TARGET).Text = JsonGet(body, "id")
    Case 9
        body = HttpCall("GET", "/api/account/2fa/status", "", mAccess, st)
    Case 10
        body = HttpCall("POST", "/api/account/2fa/setup", "", mAccess, st)
        If st = 200 Then mKey = JsonGet(body, "sharedKey"): AppendLog "Authenticator 앱에 수동 키 입력: " & mKey
    Case 11
        body = HttpCall("POST", "/api/account/2fa/enable", JsonObj("code", Code()), mAccess, st)
        If st = 200 Then AppendLog "2FA가 활성화되어 기존 토큰이 폐기되었습니다. 다시 로그인 → TOTP 인증 하십시오."
    Case 12
        body = HttpCall("POST", "/api/account/2fa/disable", JsonObj("password", txtF(F_PW).Text, "code", Code()), mAccess, st)
    Case 13
        body = HttpCall("POST", "/api/account/2fa/reset", JsonObj("password", txtF(F_PW).Text, "code", Code()), mAccess, st)
    Case 14
        body = HttpCall("POST", "/api/account/2fa/recovery-codes/regenerate", JsonObj("code", Code()), mAccess, st)
    Case 15
        body = HttpCall("POST", "/api/account/change-password", JsonObj("currentPassword", txtF(F_PW).Text, "newPassword", txtF(F_NEWPW).Text), mAccess, st)
        If st = 204 Then txtF(F_PW).Text = txtF(F_NEWPW).Text: AppendLog "비밀번호를 바꿨습니다. 기존 토큰은 폐기되었습니다."
    Case 16
        body = HttpCall("POST", "/api/auth/forgot-password", JsonObj("email", e), "", st)
    Case 17
        info = Between(LastMailBody(e, "Password reset"), "Reset token: ", vbCrLf)
        If Len(info) = 0 Then AppendLog "재설정 메일을 찾지 못했습니다: " & e: ok = False
        If ok Then body = HttpCall("POST", "/api/auth/reset-password", JsonObj("email", e, "token", info, "newPassword", txtF(F_NEWPW).Text), "", st)
        If ok And st = 204 Then txtF(F_PW).Text = txtF(F_NEWPW).Text
    Case 18
        body = HttpCall("POST", "/api/admin/users/2fa/reset", JsonObj("userId", txtF(F_TARGET).Text, "reason", txtF(F_REASON).Text), mAccess, st)
    Case 19
        If Len(mKey) = 0 Then AppendLog "먼저 2FA Setup을 실행하십시오." Else AppendLog "현재 TOTP: " & Totp(mKey)
        ok = False
    Case 20
        AppendLog "---------- 전체 시나리오 시작 ----------"
        RunScenario
        ok = False
    Case 21
        txtLog.Text = ""
        ok = False
    End Select
    If ok Then
        SaveTokens body
        AppendLog "[" & cmdApi(Index).Caption & "] " & st & " " & MaskTokens(body)
    End If
    Screen.MousePointer = vbDefault
    Exit Sub
Fail:
    Screen.MousePointer = vbDefault
    AppendLog "[" & cmdApi(Index).Caption & "] 오류: " & Err.Description
End Sub
