Attribute VB_Name = "modHttp"
Option Explicit

' API 기본 주소. 예: http://localhost:5080
Public gBaseUrl As String

' WinHttp로 REST API를 호출한다. status에 HTTP 상태 코드(연결 실패 시 -1)를 돌려준다.
Public Function HttpCall(ByVal method As String, ByVal path As String, ByVal body As String, ByVal bearer As String, ByRef status As Long) As String
    Dim r As Object
    On Error GoTo Fail
    Set r = CreateObject("WinHttp.WinHttpRequest.5.1")
    r.Open method, gBaseUrl & path, False
    r.SetTimeouts 5000, 5000, 30000, 30000
    r.SetRequestHeader "Accept", "application/json"
    If Len(bearer) > 0 Then r.SetRequestHeader "Authorization", "Bearer " & bearer
    If Len(body) > 0 Then
        r.SetRequestHeader "Content-Type", "application/json; charset=utf-8"
        r.Send Utf8Bytes(body)
    Else
        r.Send
    End If
    status = r.Status
    HttpCall = r.ResponseText
    Exit Function
Fail:
    status = -1
    HttpCall = "HTTP 오류: " & Err.Description
End Function

' GET 응답 본문(이미지 등 바이너리)을 그대로 파일에 저장한다. 200이면 True. status에 HTTP 상태(연결 실패 시 -1).
Public Function HttpGetFile(ByVal path As String, ByVal bearer As String, ByVal filePath As String, ByRef status As Long) As Boolean
    Dim r As Object, b() As Byte, f As Integer
    On Error GoTo Fail
    Set r = CreateObject("WinHttp.WinHttpRequest.5.1")
    r.Open "GET", gBaseUrl & path, False
    r.SetTimeouts 5000, 5000, 30000, 30000
    If Len(bearer) > 0 Then r.SetRequestHeader "Authorization", "Bearer " & bearer
    r.Send
    status = r.Status
    If status <> 200 Then Exit Function
    b = r.ResponseBody
    f = FreeFile
    Open filePath For Binary Access Write As #f
    Put #f, , b
    Close #f
    HttpGetFile = True
    Exit Function
Fail:
    status = -1
End Function

' 한글 등 비ASCII 문자가 깨지지 않도록 요청 본문을 UTF-8 바이트(BOM 제외)로 보낸다.
Private Function Utf8Bytes(ByVal s As String) As Variant
    Dim st As Object
    Set st = CreateObject("ADODB.Stream")
    st.Type = 2
    st.Charset = "utf-8"
    st.Open
    st.WriteText s
    st.Position = 0
    st.Type = 1
    st.Position = 3
    Utf8Bytes = st.Read
    st.Close
End Function

' 로그에 토큰 원문이 남지 않도록 accessToken/refreshToken/challengeToken 값을 앞 8자만 남기고 가린다.
Public Function MaskTokens(ByVal json As String) As String
    Dim keys As Variant, k As Variant, p As Long, q As Long, marker As String
    keys = Array("accessToken", "refreshToken", "challengeToken")
    For Each k In keys
        marker = """" & k & """:"""
        p = InStr(1, json, marker, vbBinaryCompare)
        If p > 0 Then
            p = p + Len(marker)
            q = InStr(p, json, """", vbBinaryCompare)
            If q - p > 8 Then json = Left$(json, p + 7) & "..." & Mid$(json, q)
        End If
    Next
    MaskTokens = json
End Function
