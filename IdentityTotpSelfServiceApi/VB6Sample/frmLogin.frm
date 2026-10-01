VERSION 5.00
Begin VB.Form frmLogin
   BorderStyle     =   1  'Fixed Single
   Caption         =   "Identity API 로그인 샘플"
   ClientHeight    =   3180
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
   ScaleHeight     =   3180
   ScaleWidth      =   5520
   StartUpPosition =   2  'CenterScreen
   Begin VB.CommandButton cmdLogout
      Caption         =   "로그아웃"
      Height          =   420
      Left            =   4080
      TabIndex        =   9
      Top             =   1920
      Width           =   1260
   End
   Begin VB.CommandButton cmdRefresh
      Caption         =   "토큰 갱신"
      Height          =   420
      Left            =   2760
      TabIndex        =   8
      Top             =   1920
      Width           =   1260
   End
   Begin VB.CommandButton cmdMe
      Caption         =   "내 정보"
      Height          =   420
      Left            =   1440
      TabIndex        =   7
      Top             =   1920
      Width           =   1260
   End
   Begin VB.CommandButton cmdLogin
      Caption         =   "로그인"
      Default         =   -1  'True
      Height          =   420
      Left            =   120
      TabIndex        =   6
      Top             =   1920
      Width           =   1260
   End
   Begin VB.TextBox txtPassword
      Height          =   345
      IMEMode         =   3  'DISABLE
      Left            =   1320
      PasswordChar    =   "*"
      TabIndex        =   5
      Top             =   1200
      Width           =   4020
   End
   Begin VB.TextBox txtEmail
      Height          =   345
      Left            =   1320
      TabIndex        =   3
      Top             =   720
      Width           =   4020
   End
   Begin VB.TextBox txtBaseUrl
      Height          =   345
      Left            =   1320
      TabIndex        =   1
      Text            =   "http://localhost:5080"
      Top             =   240
      Width           =   4020
   End
   Begin VB.Label lblStatus
      Caption         =   "로그인 안 됨"
      Height          =   300
      Left            =   120
      TabIndex        =   10
      Top             =   2580
      Width           =   5220
   End
   Begin VB.Label lblPassword
      Caption         =   "비밀번호"
      Height          =   300
      Left            =   120
      TabIndex        =   4
      Top             =   1260
      Width           =   1100
   End
   Begin VB.Label lblEmail
      Caption         =   "이메일"
      Height          =   300
      Left            =   120
      TabIndex        =   2
      Top             =   780
      Width           =   1100
   End
   Begin VB.Label lblBaseUrl
      Caption         =   "API 주소"
      Height          =   300
      Left            =   120
      TabIndex        =   0
      Top             =   300
      Width           =   1100
   End
End
Attribute VB_Name = "frmLogin"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' modIdentityApi를 사용하는 최소 로그인 화면 샘플.
' 운영에서는 API 주소를 HTTPS로 지정하고, 서버 인증서 검증을 끄지 않는다.

Private Sub Form_Load()
    UpdateStatus
End Sub

Private Sub cmdLogin_Click()
    Dim need2FA As Boolean, challenge As String, code As String
    ApiInit txtBaseUrl.Text
    If Not ApiLogin(txtEmail.Text, txtPassword.Text, need2FA, challenge) Then
        If gLastStatus = 423 Then
            MsgBox "로그인 실패가 반복되어 계정이 잠겼습니다. 잠시 후 다시 시도하십시오.", vbExclamation
        ElseIf gLastStatus = 0 Then
            MsgBox "서버에 연결할 수 없습니다: " & gBaseUrl, vbExclamation
        Else
            MsgBox "로그인 실패 (HTTP " & gLastStatus & ")", vbExclamation
        End If
        Exit Sub
    End If
    If need2FA Then
        ' challenge는 2차 인증 전용 토큰이다. Access Token으로 쓸 수 없다.
        code = InputBox("Authenticator 앱의 6자리 코드를 입력하십시오.", "2단계 인증")
        If Len(code) = 0 Then Exit Sub
        If Not ApiTotp(challenge, code) Then
            If gLastStatus = 423 Then
                MsgBox "인증 실패가 반복되어 계정이 잠겼습니다.", vbExclamation
            Else
                MsgBox "2단계 인증 실패 (HTTP " & gLastStatus & ")", vbExclamation
            End If
            Exit Sub
        End If
    End If
    UpdateStatus
    MsgBox "로그인 성공", vbInformation
End Sub

Private Sub cmdMe_Click()
    Dim s As String, st As Long
    ApiInit txtBaseUrl.Text
    s = ApiGet("/api/account/me", st)
    ' 401이면 Refresh를 한 번만 시도한다. 그래도 실패하면 다시 로그인해야 한다(무한 재시도 금지).
    If st = 401 And Len(gRefreshToken) > 0 Then
        If ApiRefresh() Then s = ApiGet("/api/account/me", st)
    End If
    UpdateStatus
    If st = 200 Then
        MsgBox s, vbInformation, "내 정보"
    Else
        gAccessToken = "": gRefreshToken = ""
        UpdateStatus
        MsgBox "인증이 만료되었습니다. 다시 로그인하십시오. (HTTP " & st & ")", vbExclamation
    End If
End Sub

Private Sub cmdRefresh_Click()
    ApiInit txtBaseUrl.Text
    If ApiRefresh() Then
        MsgBox "토큰 갱신 성공", vbInformation
    Else
        ' 회전된(이전) Refresh Token은 다시 쓸 수 없다. 실패하면 로그인 화면으로 돌아간다.
        gAccessToken = "": gRefreshToken = ""
        MsgBox "토큰 갱신 실패 (HTTP " & gLastStatus & "). 다시 로그인하십시오.", vbExclamation
    End If
    UpdateStatus
End Sub

Private Sub cmdLogout_Click()
    ApiInit txtBaseUrl.Text
    If ApiLogout() Then MsgBox "로그아웃 완료", vbInformation Else MsgBox "로그아웃 실패 (HTTP " & gLastStatus & ")", vbExclamation
    UpdateStatus
End Sub

Private Sub UpdateStatus()
    ' 토큰 원문은 화면에 표시하지 않는다.
    If Len(gAccessToken) > 0 Then
        lblStatus.Caption = "로그인됨 (" & gBaseUrl & ")"
    Else
        lblStatus.Caption = "로그인 안 됨"
    End If
End Sub
