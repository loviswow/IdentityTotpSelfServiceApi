VERSION 5.00
Begin VB.Form frmLogin
   BorderStyle     =   1  'Fixed Single
   Caption         =   "로그인"
   ClientHeight    =   3600
   ClientLeft      =   45
   ClientTop       =   390
   ClientWidth     =   5520
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
   MaxButton       =   0   'False
   ScaleHeight     =   3600
   ScaleWidth      =   5520
   StartUpPosition =   2  'CenterScreen
   Begin VB.Timer tmrOtp
      Enabled         =   0   'False
      Interval        =   1000
      Left            =   5040
      Top             =   0
   End
   Begin VB.Frame fraMain
      BorderStyle     =   0  'None
      Height          =   3480
      Left            =   0
      TabIndex        =   20
      Top             =   0
      Visible         =   0   'False
      Width           =   5520
      Begin VB.CommandButton cmdLogout
         Caption         =   "로그아웃"
         Height          =   420
         Left            =   4080
         TabIndex        =   26
         Top             =   2880
         Width           =   1260
      End
      Begin VB.CommandButton cmdRefresh
         Caption         =   "토큰 갱신"
         Height          =   420
         Left            =   1440
         TabIndex        =   25
         Top             =   2880
         Width           =   1260
      End
      Begin VB.CommandButton cmdMe
         Caption         =   "내 정보"
         Height          =   420
         Left            =   120
         TabIndex        =   24
         Top             =   2880
         Width           =   1260
      End
      Begin VB.Label lblStatus
         Caption         =   "로그인 안 됨"
         ForeColor       =   &H00808080&
         Height          =   300
         Left            =   120
         TabIndex        =   23
         Top             =   2400
         Width           =   5220
      End
      Begin VB.Label lblWelcome
         Caption         =   "lblWelcome"
         Height          =   1200
         Left            =   120
         TabIndex        =   22
         Top             =   600
         Width           =   5220
      End
      Begin VB.Label lblStep3
         Caption         =   "로그인 완료"
         BeginProperty Font
            Name            =   "맑은 고딕"
            Size            =   12
            Charset         =   129
            Weight          =   700
            Underline       =   0   'False
            Italic          =   0   'False
            Strikethrough   =   0   'False
         EndProperty
         Height          =   360
         Left            =   120
         TabIndex        =   21
         Top             =   120
         Width           =   5220
      End
   End
   Begin VB.Frame fraOtp
      BorderStyle     =   0  'None
      Height          =   3480
      Left            =   0
      TabIndex        =   10
      Top             =   0
      Visible         =   0   'False
      Width           =   5520
      Begin VB.CommandButton cmdVerify
         Caption         =   "인증"
         Height          =   420
         Left            =   4080
         TabIndex        =   13
         Top             =   2880
         Width           =   1260
      End
      Begin VB.CommandButton cmdUseRecovery
         Caption         =   "복구 코드 사용"
         Height          =   420
         Left            =   1440
         TabIndex        =   14
         Top             =   2880
         Width           =   1620
      End
      Begin VB.CommandButton cmdBack
         Caption         =   "< 처음으로"
         Height          =   420
         Left            =   120
         TabIndex        =   15
         Top             =   2880
         Width           =   1260
      End
      Begin VB.TextBox txtOtp
         Alignment       =   2  'Center
         BeginProperty Font
            Name            =   "Consolas"
            Size            =   20.25
            Charset         =   0
            Weight          =   700
            Underline       =   0   'False
            Italic          =   0   'False
            Strikethrough   =   0   'False
         EndProperty
         Height          =   600
         IMEMode         =   3  'DISABLE
         Left            =   1320
         MaxLength       =   6
         TabIndex        =   12
         Top             =   1200
         Width           =   2880
      End
      Begin VB.Label lblOtpError
         ForeColor       =   &H000000C0&
         Height          =   480
         Left            =   120
         TabIndex        =   18
         Top             =   2280
         Width           =   5220
      End
      Begin VB.Label lblTimer
         Alignment       =   2  'Center
         Caption         =   "남은 시간 5:00"
         ForeColor       =   &H00808080&
         Height          =   300
         Left            =   1320
         TabIndex        =   17
         Top             =   1890
         Width           =   2880
      End
      Begin VB.Label lblOtpGuide
         Caption         =   "lblOtpGuide"
         Height          =   660
         Left            =   120
         TabIndex        =   16
         Top             =   480
         Width           =   5220
      End
      Begin VB.Label lblStep2
         Caption         =   "2단계 · Google OTP 인증"
         BeginProperty Font
            Name            =   "맑은 고딕"
            Size            =   12
            Charset         =   129
            Weight          =   700
            Underline       =   0   'False
            Italic          =   0   'False
            Strikethrough   =   0   'False
         EndProperty
         Height          =   360
         Left            =   120
         TabIndex        =   11
         Top             =   120
         Width           =   5220
      End
   End
   Begin VB.Frame fraLogin
      BorderStyle     =   0  'None
      Height          =   3480
      Left            =   0
      TabIndex        =   0
      Top             =   0
      Width           =   5520
      Begin VB.CommandButton cmdNext
         Caption         =   "다음 >"
         Default         =   -1  'True
         Height          =   420
         Left            =   4080
         TabIndex        =   8
         Top             =   2880
         Width           =   1260
      End
      Begin VB.TextBox txtPassword
         Height          =   345
         IMEMode         =   3  'DISABLE
         Left            =   1320
         PasswordChar    =   "*"
         TabIndex        =   7
         Top             =   1560
         Width           =   4020
      End
      Begin VB.TextBox txtEmail
         Height          =   345
         Left            =   1320
         TabIndex        =   5
         Top             =   1080
         Width           =   4020
      End
      Begin VB.TextBox txtBaseUrl
         Height          =   345
         Left            =   1320
         TabIndex        =   3
         Text            =   "http://localhost:5080"
         Top             =   600
         Width           =   4020
      End
      Begin VB.Label lblLoginError
         ForeColor       =   &H000000C0&
         Height          =   600
         Left            =   120
         TabIndex        =   9
         Top             =   2100
         Width           =   5220
      End
      Begin VB.Label lblPassword
         Caption         =   "비밀번호"
         Height          =   300
         Left            =   120
         TabIndex        =   6
         Top             =   1620
         Width           =   1100
      End
      Begin VB.Label lblEmail
         Caption         =   "이메일"
         Height          =   300
         Left            =   120
         TabIndex        =   4
         Top             =   1140
         Width           =   1100
      End
      Begin VB.Label lblBaseUrl
         Caption         =   "API 주소"
         Height          =   300
         Left            =   120
         TabIndex        =   2
         Top             =   660
         Width           =   1100
      End
      Begin VB.Label lblStep1
         Caption         =   "1단계 · 이메일과 비밀번호"
         BeginProperty Font
            Name            =   "맑은 고딕"
            Size            =   12
            Charset         =   129
            Weight          =   700
            Underline       =   0   'False
            Italic          =   0   'False
            Strikethrough   =   0   'False
         EndProperty
         Height          =   360
         Left            =   120
         TabIndex        =   1
         Top             =   120
         Width           =   5220
      End
   End
End
Attribute VB_Name = "frmLogin"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' modIdentityApi를 사용하는 2단계 로그인 화면 샘플.
'   1단계: 이메일 + 비밀번호  → 2FA 사용자는 challengeToken을 받는다(Access Token 아님).
'   2단계: Google OTP(Authenticator) 6자리 또는 복구 코드 → Access/Refresh Token 발급.
' 운영에서는 API 주소를 HTTPS로 지정하고, 서버 인증서 검증을 끄지 않는다. 토큰은 화면·로그에 표시하지 않는다.

' challengeToken 유효 시간. 서버 설정 Jwt:TwoFactorChallengeMinutes(기본 5분)와 맞춘다.
Private Const CHALLENGE_SECONDS As Long = 300

Private mChallenge As String      ' 2단계 전용 토큰. 성공·취소·만료 시 지운다.
Private mDeadline As Date         ' challenge 만료 시각
Private mEmail As String
Private mRecoveryMode As Boolean  ' True면 OTP 대신 복구 코드 입력
Private mBusy As Boolean          ' 자동 제출 중복 방지

Private Sub Form_Load()
    ShowStep 1
End Sub

Private Sub Form_Unload(Cancel As Integer)
    ' 창을 닫으면 이 기기의 세션(Refresh Token)을 서버에서 폐기한다.
    If Len(gRefreshToken) > 0 Then ApiLogout
End Sub

' ---------- 1단계: 이메일 + 비밀번호 ----------
Private Sub cmdNext_Click()
    Dim need2FA As Boolean, challenge As String
    lblLoginError.Caption = ""
    If Len(Trim$(txtEmail.Text)) = 0 Or Len(txtPassword.Text) = 0 Then
        lblLoginError.Caption = "이메일과 비밀번호를 입력하십시오."
        Exit Sub
    End If
    ApiInit Trim$(txtBaseUrl.Text)
    SetBusy True
    If Not ApiLogin(Trim$(txtEmail.Text), txtPassword.Text, need2FA, challenge, DeviceName()) Then
        SetBusy False
        Select Case gLastStatus
        Case 0: lblLoginError.Caption = "서버에 연결할 수 없습니다: " & gBaseUrl
        Case 401: lblLoginError.Caption = "이메일 또는 비밀번호가 올바르지 않습니다. (이메일 확인을 마치지 않은 계정도 로그인할 수 없습니다.)"
        Case 423: lblLoginError.Caption = "로그인 실패가 반복되어 계정이 잠겼습니다. 15분 뒤 다시 시도하십시오."
        Case 429: lblLoginError.Caption = "요청이 너무 많습니다. 잠시 후 다시 시도하십시오."
        Case Else: lblLoginError.Caption = "로그인 실패 (HTTP " & gLastStatus & ")"
        End Select
        txtPassword.SelStart = 0: txtPassword.SelLength = Len(txtPassword.Text)
        SafeFocus txtPassword
        Exit Sub
    End If
    SetBusy False
    mEmail = Trim$(txtEmail.Text)
    txtPassword.Text = ""    ' 비밀번호는 더 이상 필요 없으므로 화면에 남기지 않는다.
    If need2FA Then
        mChallenge = challenge
        mDeadline = DateAdd("s", CHALLENGE_SECONDS, Now)
        ShowStep 2
    Else
        ShowStep 3
        lblWelcome.Caption = mEmail & " 님, 로그인되었습니다." & vbCrLf & vbCrLf & _
            "이 계정은 2단계 인증(Google OTP)이 꺼져 있습니다. 보안을 위해 2단계 인증 등록을 권장합니다."
    End If
End Sub

' ---------- 2단계: Google OTP / 복구 코드 ----------
Private Sub txtOtp_KeyPress(KeyAscii As Integer)
    ' OTP는 숫자만 받는다. 복구 코드는 영문·숫자·하이픈.
    If KeyAscii < 32 Then Exit Sub
    If Not mRecoveryMode Then
        If KeyAscii < 48 Or KeyAscii > 57 Then KeyAscii = 0
    End If
End Sub

Private Sub txtOtp_Change()
    ' 6자리가 채워지면 바로 인증한다(Google OTP 앱에서 옮겨 적는 수고를 줄인다).
    If Not mRecoveryMode And Not mBusy And Len(OnlyDigits(txtOtp.Text)) = 6 Then cmdVerify_Click
End Sub

Private Sub cmdVerify_Click()
    Dim code As String, ok As Boolean
    If mBusy Then Exit Sub
    lblOtpError.Caption = ""
    If Len(mChallenge) = 0 Or Now >= mDeadline Then
        ExpireChallenge
        Exit Sub
    End If
    If mRecoveryMode Then
        code = Trim$(txtOtp.Text)
        If Len(code) = 0 Then lblOtpError.Caption = "복구 코드를 입력하십시오.": Exit Sub
    Else
        code = OnlyDigits(txtOtp.Text)
        If Len(code) <> 6 Then lblOtpError.Caption = "Google OTP 앱의 6자리 숫자를 입력하십시오.": Exit Sub
    End If

    SetBusy True
    If mRecoveryMode Then ok = ApiRecovery(mChallenge, code, DeviceName()) Else ok = ApiTotp(mChallenge, code, DeviceName())
    SetBusy False

    If ok Then
        mChallenge = "": tmrOtp.Enabled = False
        ShowStep 3
        If mRecoveryMode Then
            lblWelcome.Caption = mEmail & " 님, 복구 코드로 로그인되었습니다." & vbCrLf & vbCrLf & _
                "사용한 복구 코드는 다시 쓸 수 없습니다. 휴대폰을 바꿨다면 Authenticator를 다시 등록하십시오."
        Else
            lblWelcome.Caption = mEmail & " 님, 2단계 인증(Google OTP)으로 로그인되었습니다."
        End If
        Exit Sub
    End If

    Select Case gLastStatus
    Case 401
        If InStr(1, gLastMessage, "Invalid", vbTextCompare) > 0 Then
            ' 코드만 틀렸다. challenge는 아직 유효하므로 다시 입력받는다(틀린 횟수는 로그인 실패와 합산되어 5회면 잠김).
            If mRecoveryMode Then
                lblOtpError.Caption = "복구 코드가 올바르지 않거나 이미 사용한 코드입니다."
            Else
                lblOtpError.Caption = "인증번호가 올바르지 않습니다. 앱에 표시된 현재 번호를 입력하십시오. (번호가 바뀌기 직전이면 다음 번호를 기다리십시오.)"
            End If
            txtOtp.Text = ""
            SafeFocus txtOtp
        Else
            ExpireChallenge    ' challenge 만료, 또는 그 사이 비밀번호·2FA 설정이 바뀜
        End If
    Case 423
        BackToLogin "인증 실패가 반복되어 계정이 잠겼습니다. 15분 뒤 다시 시도하십시오."
    Case 0
        lblOtpError.Caption = "서버에 연결할 수 없습니다: " & gBaseUrl
    Case Else
        lblOtpError.Caption = "인증 실패 (HTTP " & gLastStatus & ")"
    End Select
End Sub

Private Sub cmdUseRecovery_Click()
    mRecoveryMode = Not mRecoveryMode
    txtOtp.Text = "": lblOtpError.Caption = ""
    If mRecoveryMode Then
        txtOtp.MaxLength = 20
        cmdUseRecovery.Caption = "Google OTP 사용"
        lblOtpGuide.Caption = "계정: " & mEmail & vbCrLf & "2단계 인증을 켤 때 받은 복구 코드(예: abcde-12345) 하나를 입력하십시오."
    Else
        txtOtp.MaxLength = 6
        cmdUseRecovery.Caption = "복구 코드 사용"
        lblOtpGuide.Caption = "계정: " & mEmail & vbCrLf & "Google OTP 앱에 표시된 6자리 숫자를 입력하십시오."
    End If
    SafeFocus txtOtp
End Sub

Private Sub cmdBack_Click()
    BackToLogin ""
End Sub

Private Sub tmrOtp_Timer()
    Dim secs As Long
    secs = DateDiff("s", Now, mDeadline)
    If secs <= 0 Then
        ExpireChallenge
    Else
        lblTimer.Caption = "남은 시간 " & (secs \ 60) & ":" & Format$(secs Mod 60, "00")
        If secs <= 30 Then lblTimer.ForeColor = &HC0& Else lblTimer.ForeColor = &H808080
    End If
End Sub

Private Sub ExpireChallenge()
    BackToLogin "인증 시간(5분)이 지났습니다. 비밀번호부터 다시 로그인하십시오."
End Sub

Private Sub BackToLogin(ByVal message As String)
    mChallenge = "": tmrOtp.Enabled = False
    ShowStep 1
    lblLoginError.Caption = message
End Sub

' ---------- 로그인 후 ----------
Private Sub cmdMe_Click()
    Dim s As String, st As Long
    s = ApiGet("/api/account/me", st)
    ' 401이면 Refresh를 한 번만 시도한다. 그래도 실패하면 다시 로그인해야 한다(무한 재시도 금지).
    If st = 401 And Len(gRefreshToken) > 0 Then
        If ApiRefresh() Then s = ApiGet("/api/account/me", st)
    End If
    If st = 200 Then
        MsgBox s, vbInformation, "내 정보"
    Else
        gAccessToken = "": gRefreshToken = ""
        BackToLogin "인증이 만료되었습니다. 다시 로그인하십시오. (HTTP " & st & ")"
    End If
End Sub

Private Sub cmdRefresh_Click()
    If ApiRefresh() Then
        lblStatus.Caption = "토큰을 갱신했습니다. (" & Format$(Now, "hh:nn:ss") & ")"
    Else
        ' 회전된(이전) Refresh Token은 다시 쓸 수 없다. 실패하면 로그인 화면으로 돌아간다.
        gAccessToken = "": gRefreshToken = ""
        BackToLogin "토큰 갱신에 실패했습니다(HTTP " & gLastStatus & "). 다시 로그인하십시오."
    End If
End Sub

Private Sub cmdLogout_Click()
    ApiLogout
    gAccessToken = "": gRefreshToken = ""
    BackToLogin "로그아웃했습니다."
End Sub

' ---------- 공통 ----------
Private Sub ShowStep(ByVal stepNo As Integer)
    fraLogin.Visible = (stepNo = 1)
    fraOtp.Visible = (stepNo = 2)
    fraMain.Visible = (stepNo = 3)
    Select Case stepNo
    Case 1
        Me.Caption = "로그인 - 1단계"
        cmdNext.Default = True
        If Len(txtEmail.Text) = 0 Then SafeFocus txtEmail Else SafeFocus txtPassword
    Case 2
        Me.Caption = "로그인 - 2단계 인증"
        mRecoveryMode = False
        txtOtp.MaxLength = 6: txtOtp.Text = ""
        cmdUseRecovery.Caption = "복구 코드 사용"
        lblOtpError.Caption = ""
        lblOtpGuide.Caption = "계정: " & mEmail & vbCrLf & "Google OTP 앱에 표시된 6자리 숫자를 입력하십시오."
        lblTimer.ForeColor = &H808080
        tmrOtp_Timer
        tmrOtp.Enabled = True
        cmdVerify.Default = True
        SafeFocus txtOtp
    Case 3
        Me.Caption = "로그인됨"
        lblStatus.Caption = "서버: " & gBaseUrl
        SafeFocus cmdMe
    End Select
End Sub

Private Sub SetBusy(ByVal busy As Boolean)
    mBusy = busy
    Screen.MousePointer = IIf(busy, vbHourglass, vbDefault)
    cmdNext.Enabled = Not busy
    cmdVerify.Enabled = Not busy
End Sub

' 폼이 아직 보이지 않을 때(Form_Load 중) SetFocus는 런타임 오류 5를 내므로 무시한다.
Private Sub SafeFocus(ByVal ctl As Object)
    On Error Resume Next
    ctl.SetFocus
End Sub

' 세션(로그인 기기) 목록에 보일 이름.
Private Function DeviceName() As String
    DeviceName = "VB6 샘플 " & Environ$("COMPUTERNAME")
End Function

Private Function OnlyDigits(ByVal s As String) As String
    Dim i As Long, c As String
    For i = 1 To Len(s)
        c = Mid$(s, i, 1)
        If c >= "0" And c <= "9" Then OnlyDigits = OnlyDigits & c
    Next
End Function
