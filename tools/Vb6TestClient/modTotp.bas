Attribute VB_Name = "modTotp"
Option Explicit

' RFC 6238 TOTP(HMAC-SHA1, 30초, 6자리). ASP.NET Core Identity Authenticator와 같은 코드를 만든다.
' HMAC-SHA1은 Windows CryptoAPI(advapi32)를 사용한다. 외부 라이브러리 불필요.

Private Type SYSTEMTIME
    wYear As Integer
    wMonth As Integer
    wDayOfWeek As Integer
    wDay As Integer
    wHour As Integer
    wMinute As Integer
    wSecond As Integer
    wMilliseconds As Integer
End Type

Private Type HMAC_INFO
    HashAlgid As Long
    pbInnerString As Long
    cbInnerString As Long
    pbOuterString As Long
    cbOuterString As Long
End Type

Private Declare Sub GetSystemTime Lib "kernel32" (lpSystemTime As SYSTEMTIME)
Private Declare Function CryptAcquireContext Lib "advapi32.dll" Alias "CryptAcquireContextA" (ByRef phProv As Long, ByVal pszContainer As String, ByVal pszProvider As String, ByVal dwProvType As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptReleaseContext Lib "advapi32.dll" (ByVal hProv As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptImportKey Lib "advapi32.dll" (ByVal hProv As Long, ByRef pbData As Byte, ByVal dwDataLen As Long, ByVal hPubKey As Long, ByVal dwFlags As Long, ByRef phKey As Long) As Long
Private Declare Function CryptDestroyKey Lib "advapi32.dll" (ByVal hKey As Long) As Long
Private Declare Function CryptCreateHash Lib "advapi32.dll" (ByVal hProv As Long, ByVal Algid As Long, ByVal hKey As Long, ByVal dwFlags As Long, ByRef phHash As Long) As Long
Private Declare Function CryptSetHashParam Lib "advapi32.dll" (ByVal hHash As Long, ByVal dwParam As Long, ByRef pbData As Any, ByVal dwFlags As Long) As Long
Private Declare Function CryptHashData Lib "advapi32.dll" (ByVal hHash As Long, ByRef pbData As Byte, ByVal dwDataLen As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptGetHashParam Lib "advapi32.dll" (ByVal hHash As Long, ByVal dwParam As Long, ByRef pbData As Byte, ByRef pdwDataLen As Long, ByVal dwFlags As Long) As Long
Private Declare Function CryptDestroyHash Lib "advapi32.dll" (ByVal hHash As Long) As Long

Private Const MS_ENHANCED_PROV As String = "Microsoft Enhanced Cryptographic Provider v1.0"
Private Const PROV_RSA_FULL As Long = 1
Private Const CRYPT_VERIFYCONTEXT As Long = &HF0000000
Private Const CALG_RC2 As Long = &H6602&
Private Const CALG_HMAC As Long = &H8009&
Private Const CALG_SHA1 As Long = &H8004&
Private Const PLAINTEXTKEYBLOB As Byte = &H8
Private Const CUR_BLOB_VERSION As Byte = 2
Private Const CRYPT_IPSEC_HMAC_KEY As Long = &H100&
Private Const HP_HASHVAL As Long = 2
Private Const HP_HMAC_INFO As Long = 5

' 현재 UTC 시각의 Unix 초. 로컬 시간대와 무관하도록 GetSystemTime을 쓴다.
Public Function UnixTimeUtc() As Double
    Dim st As SYSTEMTIME, d As Date
    GetSystemTime st
    d = DateSerial(st.wYear, st.wMonth, st.wDay) + TimeSerial(st.wHour, st.wMinute, st.wSecond)
    ' DateDiff("s")는 Long이라 2038년 이후 넘친다. Double로 계산한다.
    UnixTimeUtc = Int((d - DateSerial(1970, 1, 1)) * 86400# + 0.5)
End Function

Public Function Totp(ByVal sharedKey As String) As String
    Totp = TotpAt(sharedKey, UnixTimeUtc())
End Function

Public Function TotpAt(ByVal sharedKey As String, ByVal unixSeconds As Double) As String
    Dim k() As Byte, c() As Byte, h() As Byte, stepN As Double, o As Long, v As Double, i As Long
    k = Base32Decode(sharedKey)
    ReDim c(0 To 7)
    stepN = Int(unixSeconds / 30)
    For i = 7 To 0 Step -1
        c(i) = stepN - Int(stepN / 256) * 256
        stepN = Int(stepN / 256)
    Next
    h = HmacSha1(k, c)
    o = h(19) And &HF
    v = (h(o) And &H7F) * 16777216# + h(o + 1) * 65536# + h(o + 2) * 256# + h(o + 3)
    v = v - Int(v / 1000000#) * 1000000#
    TotpAt = Format$(v, "000000")
End Function

Public Function Base32Decode(ByVal s As String) As Byte()
    Const ALPHABET As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    Dim out() As Byte, n As Long, i As Long, idx As Long, buf As Long, bits As Long
    s = UCase$(Replace(Replace(s, "=", ""), " ", ""))
    ReDim out(0 To Len(s) + 1)
    For i = 1 To Len(s)
        idx = InStr(1, ALPHABET, Mid$(s, i, 1), vbBinaryCompare) - 1
        If idx >= 0 Then
            buf = ((buf * 32) Or idx) And &HFFF&
            bits = bits + 5
            If bits >= 8 Then
                bits = bits - 8
                out(n) = (buf \ (2 ^ bits)) And &HFF
                n = n + 1
            End If
        End If
    Next
    If n = 0 Then Err.Raise vbObjectError + 513, "Base32Decode", "비어 있거나 잘못된 키입니다."
    ReDim Preserve out(0 To n - 1)
    Base32Decode = out
End Function

Public Function HmacSha1(key() As Byte, data() As Byte) As Byte()
    Dim hProv As Long, hKey As Long, hHash As Long, blob() As Byte, klen As Long, i As Long
    Dim info As HMAC_INFO, h() As Byte, hl As Long, errMsg As String
    klen = UBound(key) - LBound(key) + 1
    ' PLAINTEXTKEYBLOB: BLOBHEADER(8바이트) + 키 길이(4바이트) + 키
    ReDim blob(0 To 11 + klen)
    blob(0) = PLAINTEXTKEYBLOB
    blob(1) = CUR_BLOB_VERSION
    blob(4) = CALG_RC2 And &HFF
    blob(5) = (CALG_RC2 \ &H100) And &HFF
    blob(8) = klen And &HFF
    blob(9) = (klen \ &H100) And &HFF
    For i = 0 To klen - 1
        blob(12 + i) = key(LBound(key) + i)
    Next
    ReDim h(0 To 19)
    hl = 20
    If CryptAcquireContext(hProv, vbNullString, MS_ENHANCED_PROV, PROV_RSA_FULL, CRYPT_VERIFYCONTEXT) = 0 Then errMsg = "CryptAcquireContext": GoTo Cleanup
    ' CRYPT_IPSEC_HMAC_KEY: RC2 키 길이 제한 없이 HMAC 키로 가져온다.
    If CryptImportKey(hProv, blob(0), 12 + klen, 0, CRYPT_IPSEC_HMAC_KEY, hKey) = 0 Then errMsg = "CryptImportKey": GoTo Cleanup
    If CryptCreateHash(hProv, CALG_HMAC, hKey, 0, hHash) = 0 Then errMsg = "CryptCreateHash": GoTo Cleanup
    info.HashAlgid = CALG_SHA1
    If CryptSetHashParam(hHash, HP_HMAC_INFO, info, 0) = 0 Then errMsg = "CryptSetHashParam": GoTo Cleanup
    If CryptHashData(hHash, data(LBound(data)), UBound(data) - LBound(data) + 1, 0) = 0 Then errMsg = "CryptHashData": GoTo Cleanup
    If CryptGetHashParam(hHash, HP_HASHVAL, h(0), hl, 0) = 0 Then errMsg = "CryptGetHashParam": GoTo Cleanup
Cleanup:
    If hHash <> 0 Then CryptDestroyHash hHash
    If hKey <> 0 Then CryptDestroyKey hKey
    If hProv <> 0 Then CryptReleaseContext hProv, 0
    If Len(errMsg) > 0 Then Err.Raise vbObjectError + 514, "HmacSha1", errMsg & " 실패 (Err.LastDllError=" & Err.LastDllError & ")"
    HmacSha1 = h
End Function
