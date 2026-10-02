using System.Net; using System.Net.Http.Json; using System.Text.RegularExpressions;
using IdentityTotpSelfServiceApi.Data; using IdentityTotpSelfServiceApi.Services; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging.Abstractions; using Microsoft.Extensions.Options; using Xunit;
namespace IdentityTotpSelfServiceApi.Tests;
// SMTP 메일 발송(이관 문서 35장 우선순위 2). 테스트 SMTP 서버로 실제 SMTP 대화를 검증한다.
public sealed class SmtpEmailTests(SmtpApiFactory f):IClassFixture<SmtpApiFactory>
{
 [Fact] public async Task SmtpEmailSender_DeliversWithAuth_FromAndUtf8Body(){var o=Options.Create(new SmtpOptions{Host="127.0.0.1",Port=f.Smtp.Port,Security="None",UserName="u1",Password="p1",FromAddress="from@test.local",FromName="보내는이"});var to=$"direct-{Guid.NewGuid():N}@test.local";await new SmtpEmailSender(o,NullLogger<SmtpEmailSender>.Instance).SendAsync(to,"제목 테스트","본문 한글 token=abc");var m=await f.Smtp.WaitFor(x=>x.To.Contains(to));Assert.Equal("from@test.local",m.From);Assert.Equal(("u1","p1"),(m.AuthUser,m.AuthPassword));Assert.Contains("Content-Type: text/plain",m.Data);}
 // 가입 → SMTP로 받은 확인 메일의 토큰으로 이메일 확인까지 실제 경로 전체를 검증한다.
 [Fact] public async Task Register_SendsConfirmationOverSmtp_AndTokenConfirmsEmail(){var c=f.CreateClient();var email=TestUsers.Email("smtp");Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/auth/register",new{email,password=TestUsers.Pw})).StatusCode);var m=await f.Smtp.WaitFor(x=>x.To.Contains(email));Assert.Equal(("mailer","smtp-secret"),(m.AuthUser,m.AuthPassword));var body=Decode(m.Data);var r=Regex.Match(body,@"Confirmation token: ([^;\s]+); userId: (\S+)");Assert.True(r.Success,body);Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync("/api/auth/confirm-email",new{userId=r.Groups[2].Value,token=r.Groups[1].Value})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/auth/login",new{email,password=TestUsers.Pw})).StatusCode);}
 // 일시 장애(4xx)는 재시도해서 결국 전달한다.
 [Fact] public async Task TransientSmtpFailure_IsRetried(){var u=await TestUsers.Create(f,"smtp-retry");var c=f.CreateClient();var before=f.Smtp.MailAttempts;f.Smtp.FailNext=1;Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/forgot-password",new{email=u.Email})).StatusCode);var m=await f.Smtp.WaitFor(x=>x.To.Contains(u.Email!));Assert.Contains("Reset token: ",Decode(m.Data));Assert.True(f.Smtp.MailAttempts-before>=2);}
 // 배열 설정이 기본값 뒤에 덧붙여져(5,30,120,0.2,0.2) 테스트가 수십 초 재시도를 기다리던 결함. 설정값만 써야 한다.
 [Fact] public void RetryDelays_FromConfig_ReplaceDefaults(){var o=f.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;Assert.Equal(new[]{0.2,0.2},o.EffectiveRetryDelaysSeconds);Assert.Equal(new double[]{5,30,120},new SmtpOptions().EffectiveRetryDelaysSeconds);}
 // 영구 오류(5xx, 예: 없는 주소)는 재시도하지 않고 바로 email.failed로 남긴다. 재시도하면 발송 작업이 그동안 묶여 다른 메일이 늦어진다.
 [Fact] public async Task PermanentSmtpFailure_IsNotRetried_AndIsAudited(){var u=await TestUsers.Create(f,"smtp-bounce");f.Smtp.RejectRecipient=a=>a==u.Email;try{var c=f.CreateClient();Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/forgot-password",new{email=u.Email})).StatusCode);var until=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<until&&!TestUsers.Audits(f,u.Id).Any(z=>z.Type=="email.failed"))await Task.Delay(50);Assert.Contains(TestUsers.Audits(f,u.Id),z=>z.Type=="email.failed"&&z.Detail=="Password reset");await Task.Delay(1000);Assert.Equal(1,f.Smtp.RcptAttempts[u.Email!]);}finally{f.Smtp.RejectRecipient=null;}}
 // quoted-printable 등 전송 인코딩을 풀어 본문을 읽는다.
 static string Decode(string data){var msg=MimeKit.MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data)));return msg.TextBody??"";}
}
public sealed class SmtpDownTests(DownSmtpApiFactory f):IClassFixture<DownSmtpApiFactory>
{
 // SMTP 장애가 API 오류(500)나 응답 지연으로 드러나면 안 된다. 특히 forgot-password는 계정 존재 여부를 숨겨야 한다.
 [Fact] public async Task SmtpDown_RegisterAndForgotStillSucceed_AndFailureIsAudited(){var c=f.CreateClient();var email=TestUsers.Email("smtp-down");Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync("/api/auth/register",new{email,password=TestUsers.Pw})).StatusCode);var u=await TestUsers.Create(f,"smtp-down2");var sw=System.Diagnostics.Stopwatch.StartNew();Assert.Equal(HttpStatusCode.Accepted,(await c.PostAsJsonAsync("/api/auth/forgot-password",new{email=u.Email})).StatusCode);Assert.True(sw.Elapsed<TimeSpan.FromSeconds(3),$"응답이 SMTP를 기다렸습니다: {sw.Elapsed}");
  // 재시도까지 모두 실패하면 email.failed를 감사 로그에 남긴다(수신자 계정 기준, 본문·토큰 없음).
  var until=DateTime.UtcNow.AddSeconds(45);/* Windows는 닫힌 loopback 포트 연결 거부에 약 2초가 걸리고, 큐는 가입 메일(3회 시도) 다음에 이 메일을 처리한다 */(string Type,bool Success,string? Detail,string? Actor)[] a=[];while(DateTime.UtcNow<until){a=TestUsers.Audits(f,u.Id);if(a.Any(z=>z.Type=="email.failed"))break;await Task.Delay(100);}var e=Assert.Single(a,z=>z.Type=="email.failed");Assert.False(e.Success);Assert.Equal("Password reset",e.Detail);}
}
// Host만 있고 FromAddress가 없으면 기동 시 실패해야 한다(조용히 메일이 안 나가는 상태 방지).
public sealed class SmtpOptionsValidationTests
{
 sealed class NoFromFactory:ApiFactory{protected override bool UseTestMail=>false;protected override IDictionary<string,string?> ExtraConfig=>new Dictionary<string,string?>{{"Email:Smtp:Host","127.0.0.1"}};}
 [Fact] public void SmtpHostWithoutFromAddress_FailsAtStartup(){using var f=new NoFromFactory();var ex=Assert.ThrowsAny<Exception>(()=>f.CreateClient());Assert.Contains("FromAddress",ex.ToString());}
}
