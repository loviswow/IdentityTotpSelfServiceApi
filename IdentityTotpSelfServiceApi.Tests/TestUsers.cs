using System.Net; using System.Net.Http.Headers; using System.Net.Http.Json; using System.Text.Json;
using IdentityTotpSelfServiceApi.Data; using IdentityTotpSelfServiceApi.Models; using Microsoft.AspNetCore.Identity; using Microsoft.Extensions.DependencyInjection; using Xunit;
namespace IdentityTotpSelfServiceApi.Tests;
// 관리자·세션·메일 테스트가 함께 쓰는 사용자 준비/로그인 도우미. 이메일은 항상 고유하게 만든다(공유 DB).
public static class TestUsers
{
 public const string Pw="Strong!Pass123";
 public static string Email(string p)=>$"{p}-{Guid.NewGuid():N}@test.local";
 public static async Task<ApplicationUser> Create(ApiFactory f,string p="u",bool admin=false){var email=Email(p);using var s=f.Services.CreateScope();var um=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true};Assert.True((await um.CreateAsync(u,Pw)).Succeeded);if(admin){var rm=s.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();if(!await rm.RoleExistsAsync("Admin"))await rm.CreateAsync(new IdentityRole("Admin"));Assert.True((await um.AddToRoleAsync(u,"Admin")).Succeeded);}return u;}
 public static async Task<JsonElement> Login(HttpClient c,string email,string? deviceName=null,string pw=Pw){var r=await c.PostAsJsonAsync("/api/auth/login",new{email,password=pw,deviceName});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadFromJsonAsync<JsonElement>();}
 // setup → enable. 반환한 키로 TotpHelper.Generate를 호출해 2차 인증한다.
 public static async Task<string> Enable2Fa(HttpClient c,string email){var at=(await Login(c,email)).GetProperty("accessToken").GetString()!;var setup=new HttpRequestMessage(HttpMethod.Post,"/api/account/2fa/setup");setup.Headers.Authorization=new("Bearer",at);var key=(await (await c.SendAsync(setup)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;var en=new HttpRequestMessage(HttpMethod.Post,"/api/account/2fa/enable"){Content=JsonContent.Create(new{code=TotpHelper.Generate(key)})};en.Headers.Authorization=new("Bearer",at);Assert.Equal(HttpStatusCode.OK,(await c.SendAsync(en)).StatusCode);return key;}
 public static async Task<JsonElement> Login2Fa(HttpClient c,string email,string key,string? deviceName=null){var ch=(await Login(c,email,deviceName)).GetProperty("challengeToken").GetString()!;var r=await c.PostAsJsonAsync("/api/auth/2fa",new{challengeToken=ch,code=TotpHelper.Generate(key),deviceName});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadFromJsonAsync<JsonElement>();}
 public static HttpRequestMessage Req(HttpMethod m,string path,string? bearer,object? body=null){var r=new HttpRequestMessage(m,path);if(bearer is not null)r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);if(body is not null)r.Content=JsonContent.Create(body);return r;}
 public static string At(this JsonElement j)=>j.GetProperty("accessToken").GetString()!;
 public static string Rt(this JsonElement j)=>j.GetProperty("refreshToken").GetString()!;
 public static (string Type,bool Success,string? Detail,string? Actor)[] Audits(ApiFactory f,string userId){using var s=f.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<ApplicationDbContext>();return db.AuditLogs.Where(a=>a.UserId==userId).OrderBy(a=>a.Id).Select(a=>new{a.EventType,a.Success,a.Detail,a.ActorUserId}).AsEnumerable().Select(a=>(a.EventType,a.Success,a.Detail,a.ActorUserId)).ToArray();}
}
