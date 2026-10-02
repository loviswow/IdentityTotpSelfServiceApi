Attribute VB_Name = "modIdentityApi"
Option Explicit

Public gBaseUrl As String
Public gAccessToken As String
Public gRefreshToken As String
' 마지막 API 호출의 HTTP 상태 코드(연결 실패 시 0). 401/423 등 실패 사유 안내에 쓴다.
Public gLastStatus As Long
' 마지막 응답 본문의 "message" 값(없으면 ""). 같은 401이라도 원인을 구분할 때 쓴다.
'   2차 인증: "Invalid authenticator code." / "Invalid recovery code." = 코드 오류, 빈 값 = challenge 만료·무효
Public gLastMessage As String

Public Sub ApiInit(ByVal baseUrl As String)
    gBaseUrl = baseUrl
    If Right$(gBaseUrl, 1) = "/" Then gBaseUrl = Left$(gBaseUrl, Len(gBaseUrl) - 1)
End Sub

' deviceName: 세션(로그인 기기) 목록에 보일 이름(선택). 예: "영업관리 " & Environ$("COMPUTERNAME")
Public Function ApiLogin(ByVal email As String, ByVal password As String, ByRef requires2FA As Boolean, ByRef challengeToken As String, Optional ByVal deviceName As String = "") As Boolean
    Dim body As String, s As String, status As Long
    body = "{""email"":""" & JsonEscape(email) & """,""password"":""" & JsonEscape(password) & """" & DeviceJson(deviceName) & "}"
    s = HttpJson("POST", "/api/auth/login", body, "", status)
    If status <> 200 Then Exit Function
    requires2FA = JsonBool(s, "requiresTwoFactor")
    If requires2FA Then challengeToken = JsonString(s, "challengeToken") Else SaveTokenPair s
    ApiLogin = True
End Function

' 2FA 사용자는 세션이 여기서 만들어지므로 ApiLogin과 같은 deviceName을 넘긴다.
Public Function ApiTotp(ByVal challengeToken As String, ByVal code As String, Optional ByVal deviceName As String = "") As Boolean
    Dim body As String, s As String, status As Long
    body = "{""challengeToken"":""" & JsonEscape(challengeToken) & """,""code"":""" & JsonEscape(code) & """" & DeviceJson(deviceName) & "}"
    s = HttpJson("POST", "/api/auth/2fa", body, "", status)
    If status <> 200 Then Exit Function
    SaveTokenPair s: ApiTotp = True
End Function

' Authenticator를 쓸 수 없을 때 복구 코드(xxxxx-xxxxx)로 2차 인증한다. 복구 코드는 한 번만 쓸 수 있다.
Public Function ApiRecovery(ByVal challengeToken As String, ByVal recoveryCode As String, Optional ByVal deviceName As String = "") As Boolean
    Dim body As String, s As String, status As Long
    body = "{""challengeToken"":""" & JsonEscape(challengeToken) & """,""recoveryCode"":""" & JsonEscape(recoveryCode) & """" & DeviceJson(deviceName) & "}"
    s = HttpJson("POST", "/api/auth/2fa/recovery", body, "", status)
    If status <> 200 Then Exit Function
    SaveTokenPair s: ApiRecovery = True
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
    gLastMessage = JsonString(HttpJson, "message")
    Exit Function
EH:
    status = 0: gLastStatus = 0: HttpJson = "": gLastMessage = ""
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

Private Function DeviceJson(ByVal deviceName As String) As String
    If Len(deviceName) > 0 Then DeviceJson = ",""deviceName"":""" & JsonEscape(deviceName) & """"
End Function

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
