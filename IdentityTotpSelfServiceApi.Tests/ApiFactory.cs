using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IdentityTotpSelfServiceApi.Tests;
public class ApiFactory : WebApplicationFactory<Program>
{
 public TestEmailSender Mail {get;}=new(); protected virtual int AuthPermitLimit=>1000;
 protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key","TEST-KEY-0123456789012345678901234567890123456789"},{"Jwt:Issuer","test"},{"Jwt:Audience","test-clients"},{"RateLimiting:AuthPermitLimit",AuthPermitLimit.ToString()},{"RateLimiting:AuthWindowSeconds","60"}}));builder.ConfigureServices(s=>{s.RemoveAll<DbContextOptions<ApplicationDbContext>>();var sql=Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION");if(!string.IsNullOrWhiteSpace(sql))s.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(sql));else s.AddDbContext<ApplicationDbContext>(o=>o.UseInMemoryDatabase("db-"+Guid.NewGuid()));s.RemoveAll<IAppEmailSender>();s.AddSingleton<IAppEmailSender>(Mail);});}
}
public sealed class LowRateLimitApiFactory:ApiFactory{protected override int AuthPermitLimit=>2;}
