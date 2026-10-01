Attribute VB_Name = "modMain"
Option Explicit

' 실행 방법
'   화면 모드 : Vb6TestClient.exe
'   자동 모드 : Vb6TestClient.exe /auto base=http://localhost:5080 mail=D:\...\mail admin=관리자이메일:비밀번호 out=result.txt
'               전체 시나리오를 실행하고 결과를 out 파일에 쓴 뒤 종료한다. 실패가 있으면 종료 코드 1.
'   TOTP 확인 : Vb6TestClient.exe /totp key=BASE32KEY [time=유닉스초] out=code.txt

Private Declare Sub ExitProcess Lib "kernel32" (ByVal uExitCode As Long)
Public Declare Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)

Public gMailDir As String
Public gAdminEmail As String
Public gAdminPassword As String
Public gUi As Boolean
Private mLog As String

Sub Main()
    Dim args() As String, i As Long, a As String, mode As String, outFile As String, key As String, t As String, failed As Long
    gBaseUrl = "http://localhost:5080"
    args = Split(Trim$(Command$), " ")
    For i = 0 To UBound(args)
        a = args(i)
        If LCase$(a) = "/auto" Then mode = "auto"
        If LCase$(a) = "/totp" Then mode = "totp"
        If LCase$(Left$(a, 5)) = "base=" Then gBaseUrl = Mid$(a, 6)
        If LCase$(Left$(a, 5)) = "mail=" Then gMailDir = Mid$(a, 6)
        If LCase$(Left$(a, 4)) = "out=" Then outFile = Mid$(a, 5)
        If LCase$(Left$(a, 4)) = "key=" Then key = Mid$(a, 5)
        If LCase$(Left$(a, 5)) = "time=" Then t = Mid$(a, 6)
        If LCase$(Left$(a, 6)) = "admin=" Then
            gAdminEmail = Left$(Mid$(a, 7), InStr(Mid$(a, 7), ":") - 1)
            gAdminPassword = Mid$(Mid$(a, 7), InStr(Mid$(a, 7), ":") + 1)
        End If
    Next
    If Right$(gBaseUrl, 1) = "/" Then gBaseUrl = Left$(gBaseUrl, Len(gBaseUrl) - 1)

    Select Case mode
    Case "auto"
        On Error Resume Next
        failed = RunScenario()
        If Err.Number <> 0 Then LogLine "FAIL  예외: " & Err.Description: failed = failed + 1
        On Error GoTo 0
        If Len(outFile) > 0 Then WriteText outFile, mLog
        ExitProcess IIf(failed = 0, 0, 1)
    Case "totp"
        If Len(t) > 0 Then WriteText outFile, TotpAt(key, CDbl(t)) Else WriteText outFile, Totp(key)
        ExitProcess 0
    Case Else
        gUi = True
        frmMain.Show
    End Select
End Sub

Public Sub LogLine(ByVal s As String)
    mLog = mLog & s & vbCrLf
    If gUi Then frmMain.AppendLog s
End Sub

Public Sub WriteText(ByVal path As String, ByVal s As String)
    Dim f As Integer
    f = FreeFile
    Open path For Output As #f
    Print #f, s;
    Close #f
End Sub

Public Function ReadText(ByVal path As String) As String
    Dim f As Integer, b() As Byte
    f = FreeFile
    Open path For Binary Access Read As #f
    If LOF(f) > 0 Then
        ReDim b(0 To LOF(f) - 1)
        Get #f, , b
        ReadText = StrConv(b, vbUnicode)
    End If
    Close #f
End Function

' 개발용 PickupDirectory(API의 Email:PickupDirectory)에서 받는 사람/제목이 일치하는 가장 최근 메일 본문을 읽는다.
Public Function LastMailBody(ByVal email As String, ByVal subject As String) As String
    Dim fname As String, bestName As String, best As String, t As String, tries As Long, p As Long
    If Len(gMailDir) = 0 Then Exit Function
    For tries = 1 To 30
        bestName = "": best = ""
        fname = Dir$(gMailDir & "\*.txt")
        Do While Len(fname) > 0
            If fname > bestName Then
                t = ReadText(gMailDir & "\" & fname)
                If InStr(1, t, "To: " & email & vbCrLf, vbTextCompare) = 1 And InStr(1, t, vbCrLf & "Subject: " & subject & vbCrLf, vbBinaryCompare) > 0 Then
                    bestName = fname: best = t
                End If
            End If
            fname = Dir$
        Loop
        If Len(best) > 0 Then Exit For
        Sleep 100
    Next
    p = InStr(1, best, vbCrLf & vbCrLf)
    If p > 0 Then LastMailBody = Mid$(best, p + 4)
End Function

' s에서 startMarker 다음부터 endMarker 전까지. endMarker가 없으면 끝까지(앞뒤 공백/줄바꿈 제거).
Public Function Between(ByVal s As String, ByVal startMarker As String, ByVal endMarker As String) As String
    Dim p As Long, q As Long
    p = InStr(1, s, startMarker, vbBinaryCompare)
    If p = 0 Then Exit Function
    p = p + Len(startMarker)
    q = InStr(p, s, endMarker, vbBinaryCompare)
    If q = 0 Then q = Len(s) + 1
    Between = Trim$(Replace(Replace(Mid$(s, p, q - p), vbCr, ""), vbLf, ""))
End Function
