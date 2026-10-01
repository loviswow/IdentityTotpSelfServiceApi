Attribute VB_Name = "modIdentityApi"
Option Explicit

Public gBaseUrl As String
Public gAccessToken As String
Public gRefreshToken As String
' 마지막 API 호출의 HTTP 상태 코드(연결 실패 시 0). 401/423 등 실패 사유 안내에 쓴다.
Public gLastStatus As Long

Public Sub ApiInit(ByVal baseUrl As String)
    gBaseUrl = baseUrl
    If Right$(gBaseUrl, 1) = "/" Then gBaseUrl = Left$(gBaseUrl, Len(gBaseUrl) - 1)
End Sub

Public Function ApiLogin(ByVal email As String, ByVal password As String, ByRef requires2FA As Boolean, ByRef challengeToken As String) As Boolean
    Dim body As String, s As String, status As Long
    body = "{""email"":""" & JsonEscape(email) & """,""password"":""" & JsonEscape(password) & """}"
    s = HttpJson("POST", "/api/auth/login", body, "", status)
    If status <> 200 Then Exit Function
    requires2FA = JsonBool(s, "requiresTwoFactor")
    If requires2FA Then challengeToken = JsonString(s, "challengeToken") Else SaveTokenPair s
    ApiLogin = True
End Function

Public Function ApiTotp(ByVal challengeToken As String, ByVal code As String) As Boolean
    Dim body As String, s As String, status As Long
    body = "{""challengeToken"":""" & JsonEscape(challengeToken) & """,""code"":""" & JsonEscape(code) & """}"
    s = HttpJson("POST", "/api/auth/2fa", body, "", status)
    If status <> 200 Then Exit Function
    SaveTokenPair s: ApiTotp = True
End Function

Public Function ApiRefresh() As Boolean
    Dim body As String, s As String, status As Long
    body = "{""refreshToken"":""" & JsonEscape(gRefreshToken) & """}"
    s = HttpJson("POST", "/api/auth/token/refresh", body, "", status)
    If status <> 200 Then Exit Function
    SaveTokenPair s: ApiRefresh = True
End Function

Public Function ApiLogout() As Boolean
    Dim body As String, s As String, status As Long
    body = "{""refreshToken"":""" & JsonEscape(gRefreshToken) & """}"
    s = HttpJson("POST", "/api/auth/token/revoke", body, gAccessToken, status)
    If status = 204 Then gAccessToken = "": gRefreshToken = "": ApiLogout = True
End Function

Public Function ApiGet(ByVal path As String, ByRef status As Long) As String
    ApiGet = HttpJson("GET", path, "", gAccessToken, status)
End Function

Private Function HttpJson(ByVal method As String, ByVal path As String, ByVal body As String, ByVal bearer As String, ByRef status As Long) As String
    On Error GoTo EH
    Dim h As Object
    Set h = CreateObject("WinHttp.WinHttpRequest.5.1")
    h.Open method, gBaseUrl & path, False
    h.SetRequestHeader "Accept", "application/json"
    If method <> "GET" Then h.SetRequestHeader "Content-Type", "application/json; charset=utf-8"
    If Len(bearer) > 0 Then h.SetRequestHeader "Authorization", "Bearer " & bearer
    h.SetTimeouts 5000, 5000, 10000, 10000
    If method = "GET" Then h.Send Else h.Send Utf8Bytes(body)
    status = h.Status: gLastStatus = status: HttpJson = h.ResponseText
    Exit Function
EH:
    status = 0: gLastStatus = 0: HttpJson = ""
End Function

Private Function Utf8Bytes(ByVal s As String) As Variant
    Dim st As Object: Set st = CreateObject("ADODB.Stream")
    st.Type = 2: st.Charset = "utf-8": st.Open: st.WriteText s: st.Position = 0: st.Type = 1: st.Position = 3
    Utf8Bytes = st.Read: st.Close
End Function

Private Sub SaveTokenPair(ByVal json As String)
    gAccessToken = JsonString(json, "accessToken")
    gRefreshToken = JsonString(json, "refreshToken")
End Sub

Private Function JsonEscape(ByVal s As String) As String
    s = Replace(s, "\", "\\"): s = Replace(s, Chr$(34), "\" & Chr$(34)): s = Replace(s, vbCrLf, "\n")
    JsonEscape = s
End Function

' 외부 JSON 라이브러리 없이 샘플을 단독 실행하기 위한 단순 추출기.
' 운영에서는 VB-JSON 등 검증된 parser 사용 권장.
Private Function JsonString(ByVal json As String, ByVal key As String) As String
    Dim p As Long, q As Long, r As Long, marker As String
    marker = Chr$(34) & key & Chr$(34): p = InStr(1, json, marker, vbTextCompare): If p = 0 Then Exit Function
    p = InStr(p + Len(marker), json, ":"): If p = 0 Then Exit Function
    ' 값이 문자열이 아니면(null 등) 빈 문자열. 다음 속성의 문자열을 잘못 읽지 않도록 콜론 바로 뒤를 확인한다.
    q = p + 1
    Do While Mid$(json, q, 1) = " "
        q = q + 1
    Loop
    If Mid$(json, q, 1) <> Chr$(34) Then Exit Function
    r = q + 1
    Do
        r = InStr(r, json, Chr$(34)): If r = 0 Then Exit Function
        If Mid$(json, r - 1, 1) <> "\" Then Exit Do
        r = r + 1
    Loop
    JsonString = Mid$(json, q + 1, r - q - 1)
End Function
Private Function JsonBool(ByVal json As String, ByVal key As String) As Boolean
    Dim p As Long, marker As String: marker = Chr$(34) & key & Chr$(34): p = InStr(1, json, marker, vbTextCompare)
    If p = 0 Then Exit Function
    p = InStr(p + Len(marker), json, ":"): If p = 0 Then Exit Function
    ' "true," 처럼 뒤에 쉼표/괄호가 붙으므로 앞 4글자만 비교한다.
    JsonBool = (LCase$(Left$(LTrim$(Mid$(json, p + 1)), 4)) = "true")
End Function
