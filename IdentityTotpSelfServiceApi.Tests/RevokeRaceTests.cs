using System.Net; using System.Net.Http.Json; using System.Text.Json;
using IdentityTotpSelfServiceApi.Data; using IdentityTotpSelfServiceApi.Entities; using IdentityTotpSelfServiceApi.Models; using IdentityTotpSelfServiceApi.Services; using Microsoft.AspNetCore.Identity; using Microsoft.EntityFrameworkCore; using Microsoft.Extensions.DependencyInjection; using Xunit;
using static IdentityTotpSelfServiceApi.Tests.TestUsers;
namespace IdentityTotpSelfServiceApi.Tests;
// REG-013: 폐기(로그아웃·세션 폐기·전체 로그아웃·비밀번호 변경)를 읽은 뒤 저장하기 전에 같은 토큰이 회전되면
// Version 동시성 충돌로 폐기가 500이 되고 아무 토큰도 폐기되지 않았다. 탈취한 토큰으로 Refresh를 반복하는 공격자가 회전으로 살아남는다.
// BeforeSaveHook으로 폐기 저장 직전에 다른 컨텍스트에서 실제 회전(RotateAsync)을 일으켜 확정적으로 재현한다.
public sealed class RevokeRaceTests(ApiFactory f):IClassFixture<ApiFactory>
{
 [Theory,InlineData("logout"),InlineData("session"),InlineData("revoke-all"),InlineData("change-password")]
 public async Task Revoke_WhenTokenIsRotatedConcurrently_StillRevokesTheRotatedToken(string path)
 {
  var c=f.CreateClient();var u=await Create(f,"race-"+path);var l=await Login(c,u.Email!,"victim");var rt=l.Rt();string? attackerRt=null;var fired=0;
  // 이 사용자의 토큰을 폐기하는 저장이 처음 일어날 때 한 번만, 다른 요청이 먼저 그 토큰을 회전시킨다.
  BeforeSaveHook.Hook=async ctx=>{if(!ctx.ChangeTracker.Entries<RefreshToken>().Any(e=>e.State==EntityState.Modified&&e.Entity.UserId==u.Id&&e.Entity.RevokeReason!="rotated"))return;if(Interlocked.Exchange(ref fired,1)==1)return;
   using var s=f.Services.CreateScope();var um=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var r=await s.ServiceProvider.GetRequiredService<RefreshTokenService>().RotateAsync(rt,"10.0.0.66",id=>um.FindByIdAsync(id));Assert.NotNull(r.Raw);attackerRt=r.Raw;};
  try
  {
   var res=path switch{
    "logout"=>await c.PostAsJsonAsync("/api/auth/token/revoke",new{refreshToken=rt}),
    "session"=>await c.SendAsync(Req(HttpMethod.Delete,$"/api/account/sessions/{(await (await c.SendAsync(Req(HttpMethod.Get,"/api/account/sessions",l.At()))).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("sessionId").GetString()}",l.At())),
    "revoke-all"=>await c.SendAsync(Req(HttpMethod.Post,"/api/account/sessions/revoke-all",l.At())),
    _=>await c.SendAsync(Req(HttpMethod.Post,"/api/account/change-password",l.At(),new{currentPassword=Pw,newPassword="NewStrong!Pass456"}))};
   Assert.Equal(1,fired);
   Assert.Equal(HttpStatusCode.NoContent,res.StatusCode);
   // 경쟁 중에 회전으로 받은 토큰도 폐기되어야 한다.
   Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/auth/token/refresh",new{refreshToken=attackerRt})).StatusCode);
   using var s2=f.Services.CreateScope();var now=DateTimeOffset.UtcNow;Assert.False(await s2.ServiceProvider.GetRequiredService<ApplicationDbContext>().RefreshTokens.AnyAsync(x=>x.UserId==u.Id&&x.RevokedAt==null&&x.ExpiresAt>now));
  }
  finally{BeforeSaveHook.Hook=null;}
 }
}
