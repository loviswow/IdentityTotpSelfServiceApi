namespace IdentityTotpSelfServiceApi.Services;
public interface IAppEmailSender { Task SendAsync(string to,string subject,string body); }
// 개발용. 운영에서는 SMTP/SendGrid/사내 메일 구현체로 교체한다. 토큰은 로그에 기록하지 않는다.
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string to,string subject,string body) { logger.LogInformation("Development email queued to {Recipient}: {Subject}",to,subject); return Task.CompletedTask; }
}
// 개발/E2E 테스트 전용. 메일을 파일로 저장해 테스트 클라이언트가 확인·재설정 토큰을 읽을 수 있게 한다.
// 토큰 원문이 디스크에 남으므로 Program.cs에서 Development 환경 + Email:PickupDirectory 설정 시에만 등록한다.
public sealed class PickupDirectoryEmailSender(string directory) : IAppEmailSender
{
    public async Task SendAsync(string to,string subject,string body)
    {
        Directory.CreateDirectory(directory);
        var name=$"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.txt";
        await File.WriteAllTextAsync(Path.Combine(directory,name),$"To: {to}\r\nSubject: {subject}\r\n\r\n{body}\r\n");
    }
}
