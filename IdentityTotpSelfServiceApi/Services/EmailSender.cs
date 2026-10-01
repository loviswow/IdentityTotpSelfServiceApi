namespace IdentityTotpSelfServiceApi.Services;
public interface IAppEmailSender { Task SendAsync(string to,string subject,string body); }
// 개발용. 운영에서는 SMTP/SendGrid/사내 메일 구현체로 교체한다. 토큰은 로그에 기록하지 않는다.
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string to,string subject,string body) { logger.LogInformation("Development email queued to {Recipient}: {Subject}",to,subject); return Task.CompletedTask; }
}
