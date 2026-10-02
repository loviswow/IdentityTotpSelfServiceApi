using System.Threading.Channels;
using IdentityTotpSelfServiceApi.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityTotpSelfServiceApi.Services;

// 설정 Email:Smtp:*. Host가 있으면 SMTP로 발송한다. Password는 저장소에 넣지 말고 환경 변수(Email__Smtp__Password)나 Secret Store로 준다.
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    // StartTls(기본, 587) / SslOnConnect(465) / Auto / None(평문, 사내 릴레이나 테스트 전용)
    public string Security { get; set; } = "StartTls";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    // 일시 장애 시 재시도 간격(초). 항목 수만큼 재시도한다. 설정하지 않으면 DefaultRetryDelaysSeconds(1차 + 재시도 3회).
    // 기본값을 속성 초기값으로 두면 설정 바인더가 설정값을 기본값 뒤에 덧붙이므로(5,30,120,설정값...) null로 두고 사용할 때 적용한다.
    public double[]? RetryDelaysSeconds { get; set; }
    public static readonly double[] DefaultRetryDelaysSeconds = [5, 30, 120];
    public double[] EffectiveRetryDelaysSeconds => RetryDelaysSeconds is { Length: > 0 } d ? d : DefaultRetryDelaysSeconds;

    // 동시에 발송하는 작업자 수. 재시도 대기 중인 메일이 다른 메일을 막지 않게 한다.
    public int MaxConcurrency { get; set; } = 4;

    public bool Enabled => !string.IsNullOrWhiteSpace(Host);
}

// MailKit으로 한 통을 바로 보낸다. 실패하면 예외를 던진다(재시도는 EmailDispatchService가 한다).
// 서버 인증서 검증은 끄지 않는다. 본문(확인·재설정 토큰 포함)은 로그에 남기지 않는다.
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IAppEmailSender
{
    public async Task SendAsync(string to, string subject, string body)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName ?? "", o.FromAddress!));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = Math.Max(1, o.TimeoutSeconds) * 1000 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds) * 2));
        await client.ConnectAsync(o.Host!, o.Port, ParseSecurity(o.Security), cts.Token);
        if (!string.IsNullOrEmpty(o.UserName))
            await client.AuthenticateAsync(o.UserName, o.Password ?? "", cts.Token);
        await client.SendAsync(message, cts.Token);
        await client.DisconnectAsync(true, cts.Token);
        logger.LogInformation("Email sent to {Recipient}: {Subject}", to, subject);
    }

    public static SecureSocketOptions ParseSecurity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" or "ssl" or "tls" => SecureSocketOptions.SslOnConnect,
        "auto" => SecureSocketOptions.Auto,
        "none" => SecureSocketOptions.None,
        _ => throw new InvalidOperationException($"Email:Smtp:Security '{value}' is not supported (StartTls, SslOnConnect, Auto, None).")
    };
}

// 요청 처리 중에는 큐에 넣기만 한다. SMTP 응답을 기다리지 않으므로
//  - SMTP 장애가 회원가입·비밀번호 재설정 API의 500으로 번지지 않고
//  - forgot-password 응답 시간으로 계정 존재 여부가 드러나지 않는다(존재할 때만 발송하므로 동기 발송이면 느려진다).
//  - 큐가 가득 차도 예외를 던지지 않는다. 계정이 있을 때만 발송하는 forgot-password가 그때만 500이 되면 계정 존재가 드러난다.
public sealed class EmailQueue(ILogger<EmailQueue> logger) : IAppEmailSender
{
    public sealed record Item(string To, string Subject, string Body);
    private readonly Channel<Item> channel = Channel.CreateBounded<Item>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.Wait });
    public ChannelReader<Item> Reader => channel.Reader;
    public Task SendAsync(string to, string subject, string body)
    {
        if (!channel.Writer.TryWrite(new Item(to, subject, body)))
            logger.LogError("Email queue is full. Dropped email to {Recipient} ({Subject}).", to, subject);
        return Task.CompletedTask;
    }
}

// 큐에서 꺼내 SmtpEmailSender로 보낸다(작업자 MaxConcurrency개). 일시 오류는 RetryDelaysSeconds 간격으로 재시도하고, 영구 오류(5xx)는 재시도하지 않는다.
// 끝내 실패하면 오류 로그와 감사 로그(email.failed, Detail=제목)를 남긴다.
// 큐는 메모리에만 있으므로 프로세스가 내려가면 보내지 못한 메일은 사라진다. 사용자는 재발송 API로 다시 요청할 수 있다.
public sealed class EmailDispatchService(EmailQueue queue, SmtpEmailSender smtp, IOptions<SmtpOptions> options,
    IServiceScopeFactory scopes, IHostEnvironment env, ILogger<EmailDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
                logger.LogWarning("Email:Smtp:Host is not configured. Confirmation and password reset emails are NOT delivered.");
            return;
        }
        // 발송 작업자를 여러 개 둔다. 한 메일이 재시도로 기다리는 동안 다른 메일이 막히지 않게 한다.
        var workers = Math.Clamp(options.Value.MaxConcurrency, 1, 32);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
                await DeliverAsync(item, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    // 다시 보내도 결과가 같은 오류: 서버의 5xx 응답(없는 주소, 거부 등)과 주소 형식 오류.
    private static bool IsPermanent(Exception ex) =>
        ex is SmtpCommandException { StatusCode: >= (SmtpStatusCode)500 } or MimeKit.ParseException;

    private async Task DeliverAsync(EmailQueue.Item item, CancellationToken ct)
    {
        var delays = options.Value.EffectiveRetryDelaysSeconds;
        for (var attempt = 0; ; attempt++)
        {
            try { await smtp.SendAsync(item.To, item.Subject, item.Body); return; }
            catch (Exception ex) when (attempt < delays.Length && !IsPermanent(ex) && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Email to {Recipient} ({Subject}) failed on attempt {Attempt}: {Error}. Retrying.", item.To, item.Subject, attempt + 1, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delays[attempt])), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError("Email to {Recipient} ({Subject}) failed after {Attempts} attempts: {Error}", item.To, item.Subject, attempt + 1, ex.Message);
                await AuditFailureAsync(item);
                return;
            }
        }
    }

    private async Task AuditFailureAsync(EmailQueue.Item item)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(item.To);
            await scope.ServiceProvider.GetRequiredService<AuditService>().WriteAsync("email.failed", false, user?.Id, item.Subject);
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to write email.failed audit log"); }
    }
}
