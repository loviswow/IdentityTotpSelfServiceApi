using System.Net; using System.Net.Http.Json; using System.Text; using System.Text.Json;
using Xunit;
using static IdentityTotpSelfServiceApi.Tests.TestUsers;
namespace IdentityTotpSelfServiceApi.Tests;
// Authenticator 등록 QR 이미지(GET /api/account/2fa/qr). VB6처럼 QR을 직접 그리기 어려운 클라이언트용.
// QR에는 TOTP Secret이 들어 있으므로 등록 전(2FA 꺼짐 + setup 완료)에만 주고, 캐시하지 않게 한다.
public sealed class TwoFactorQrTests(ApiFactory f):IClassFixture<ApiFactory>
{
 static Task<HttpResponseMessage> Qr(HttpClient c,string? at,string? format=null)=>c.SendAsync(Req(HttpMethod.Get,"/api/account/2fa/qr"+(format is null?"":"?format="+format),at));
 [Fact] public async Task Qr_AfterSetup_ReturnsImageInEachFormat_NotCached()
 {
  var c=f.CreateClient();var u=await Create(f,"qr");var at=(await Login(c,u.Email!)).At();
  Assert.Equal(HttpStatusCode.OK,(await c.SendAsync(Req(HttpMethod.Post,"/api/account/2fa/setup",at))).StatusCode);
  foreach(var (fmt,type,magic) in new[]{((string?)null,"image/png","\x89PNG"),("png","image/png","\x89PNG"),("bmp","image/bmp","BM"),("svg","image/svg+xml","<svg")})
  {
   var r=await Qr(c,at,fmt);Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal(type,r.Content.Headers.ContentType?.MediaType);
   Assert.Contains("no-store",r.Headers.CacheControl?.ToString());
   var bytes=await r.Content.ReadAsByteArrayAsync();var head=fmt=="svg"?Encoding.UTF8.GetString(bytes):Encoding.Latin1.GetString(bytes,0,4);
   Assert.True(fmt=="svg"?head.Contains("<svg"):head.StartsWith(magic),$"{fmt}: {head[..Math.Min(20,head.Length)]}");
  }
 }
 [Fact] public async Task Qr_BeforeSetup_Returns400_AndAfterEnable_Returns409()
 {
  var c=f.CreateClient();var u=await Create(f,"qr-state");var at=(await Login(c,u.Email!)).At();
  Assert.Equal(HttpStatusCode.BadRequest,(await Qr(c,at)).StatusCode);
  var key=await Enable2Fa(c,u.Email!);var t=await Login2Fa(c,u.Email!,key);
  Assert.Equal(HttpStatusCode.Conflict,(await Qr(c,t.At())).StatusCode);
 }
 [Fact] public async Task Qr_WithoutToken_Returns401_AndBadFormat_Returns400()
 {
  var c=f.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await Qr(c,null)).StatusCode);
  var u=await Create(f,"qr-fmt");var at=(await Login(c,u.Email!)).At();await c.SendAsync(Req(HttpMethod.Post,"/api/account/2fa/setup",at));
  Assert.Equal(HttpStatusCode.BadRequest,(await Qr(c,at,"gif")).StatusCode);
 }
}
