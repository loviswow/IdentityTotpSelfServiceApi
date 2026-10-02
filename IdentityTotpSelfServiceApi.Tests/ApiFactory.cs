using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IdentityTotpSelfServiceApi.Tests;
public class ApiFactory : WebApplicationFactory<Program>
{
 // InMemory DB 이름은 factory당 한 번만 만든다. 람다 안에서 만들면 요청 scope마다 다른 DB가 된다.
 public TestEmailSender Mail {get;}=new(); protected virtual int AuthPermitLimit=>1000;
 // 하위 factory가 설정을 추가하거나(SMTP 등) 실제 메일 발송기를 그대로 쓰게 할 때 재정의한다.
 protected virtual IDictionary<string,string?> ExtraConfig=>new Dictionary<string,string?>(); protected virtual bool UseTestMail=>true;
 protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>{c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key","TEST-KEY-0123456789012345678901234567890123456789"},{"Jwt:Issuer","test"},{"Jwt:Audience","test-clients"},{"RateLimiting:AuthPermitLimit",AuthPermitLimit.ToString()},{"RateLimiting:AuthWindowSeconds","60"},{"RefreshTokens:Cleanup:Enabled","false"}});c.AddInMemoryCollection(ExtraConfig);});builder.ConfigureServices(s=>{s.RemoveAll<DbContextOptions<ApplicationDbContext>>();var sql=Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION");if(!string.IsNullOrWhiteSpace(sql))s.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(sql).AddInterceptors(new BeforeSaveHook()));else{var name="db-"+Guid.NewGuid();s.AddDbContext<ApplicationDbContext>(o=>o.UseInMemoryDatabase(name).AddInterceptors(new BeforeSaveHook()));}if(UseTestMail){s.RemoveAll<IAppEmailSender>();s.AddSingleton<IAppEmailSender>(Mail);}});}
}
public sealed class LowRateLimitApiFactory:ApiFactory{protected override int AuthPermitLimit=>2;}
// 실제 SmtpEmailSender + 발송 큐를 테스트 SMTP 서버(FakeSmtpServer)에 연결한다.
public class SmtpApiFactory:ApiFactory
{
 public FakeSmtpServer Smtp {get;}=new();
 protected override bool UseTestMail=>false;
 protected virtual int SmtpPort=>Smtp.Port;
 protected override IDictionary<string,string?> ExtraConfig=>new Dictionary<string,string?>{{"Email:Smtp:Host","127.0.0.1"},{"Email:Smtp:Port",SmtpPort.ToString()},{"Email:Smtp:Security","None"},{"Email:Smtp:UserName","mailer"},{"Email:Smtp:Password","smtp-secret"},{"Email:Smtp:FromAddress","no-reply@test.local"},{"Email:Smtp:FromName","Identity Test"},{"Email:Smtp:TimeoutSeconds","5"},{"Email:Smtp:RetryDelaysSeconds:0","0.2"},{"Email:Smtp:RetryDelaysSeconds:1","0.2"}};
 public override async ValueTask DisposeAsync(){await base.DisposeAsync();await Smtp.DisposeAsync();}
}
// SMTP 서버가 내려간 상태: 연결이 즉시 거부되는 포트를 쓴다.
public sealed class DownSmtpApiFactory:SmtpApiFactory{static readonly int closed=ClosedPort();protected override int SmtpPort=>closed;static int ClosedPort(){var l=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);l.Start();var p=((System.Net.IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}}
