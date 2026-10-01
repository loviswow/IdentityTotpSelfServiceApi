Attribute VB_Name = "modJson"
Option Explicit

' 테스트 클라이언트용 최소 JSON 처리. API 응답(System.Text.Json, 공백 없음)의 최상위 속성만 다룬다.
' 업무 프로그램에서는 VB-JSON 등 검증된 파서를 쓰는 것을 권장한다.

Public Function JsonEscape(ByVal s As String) As String
    s = Replace(s, "\", "\\")
    s = Replace(s, """", "\""")
    s = Replace(s, vbCr, "\r")
    s = Replace(s, vbLf, "\n")
    s = Replace(s, vbTab, "\t")
    JsonEscape = s
End Function

' 키, 값 순서의 인수로 {"k1":"v1","k2":"v2"} 문자열 객체를 만든다.
Public Function JsonObj(ParamArray kv() As Variant) As String
    Dim i As Long, s As String
    For i = LBound(kv) To UBound(kv) Step 2
        If Len(s) > 0 Then s = s & ","
        s = s & """" & kv(i) & """:""" & JsonEscape(CStr(kv(i + 1))) & """"
    Next
    JsonObj = "{" & s & "}"
End Function

' 최상위 속성 값을 돌려준다. 문자열은 따옴표를 벗기고, 숫자/true/false/null은 원문 그대로. 없으면 "".
Public Function JsonGet(ByVal json As String, ByVal key As String) As String
    Dim p As Long
    p = InStr(1, json, """" & key & """:", vbBinaryCompare)
    If p = 0 Then Exit Function
    p = p + Len(key) + 3
    JsonGet = ReadValue(json, p)
End Function

' 문자열 배열 속성의 index번째(0부터) 값을 돌려준다. 없으면 "".
Public Function JsonArrayItem(ByVal json As String, ByVal key As String, ByVal index As Long) As String
    Dim p As Long, i As Long, v As String
    p = InStr(1, json, """" & key & """:[", vbBinaryCompare)
    If p = 0 Then Exit Function
    p = p + Len(key) + 4
    For i = 0 To index
        Do While Mid$(json, p, 1) = "," Or Mid$(json, p, 1) = " "
            p = p + 1
        Loop
        If Mid$(json, p, 1) = "]" Or p > Len(json) Then Exit Function
        v = ReadValue(json, p)
    Next
    JsonArrayItem = v
End Function

Private Function ReadValue(ByVal json As String, ByRef p As Long) As String
    Dim c As String, s As String, e As Long
    Do While Mid$(json, p, 1) = " "
        p = p + 1
    Loop
    If Mid$(json, p, 1) = """" Then
        p = p + 1
        Do While p <= Len(json)
            c = Mid$(json, p, 1)
            If c = "\" Then
                c = Mid$(json, p + 1, 1)
                Select Case c
                    Case "n": s = s & vbLf
                    Case "r": s = s & vbCr
                    Case "t": s = s & vbTab
                    Case "u": s = s & ChrW$(CLng("&H" & Mid$(json, p + 2, 4))): p = p + 4
                    Case Else: s = s & c
                End Select
                p = p + 2
            ElseIf c = """" Then
                p = p + 1
                Exit Do
            Else
                s = s & c
                p = p + 1
            End If
        Loop
        ReadValue = s
    Else
        e = p
        Do While e <= Len(json)
            c = Mid$(json, e, 1)
            If c = "," Or c = "}" Or c = "]" Then Exit Do
            e = e + 1
        Loop
        ReadValue = Trim$(Mid$(json, p, e - p))
        p = e
    End If
End Function
