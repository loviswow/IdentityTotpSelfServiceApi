using IdentityTotpSelfServiceApi.Services;
namespace IdentityTotpSelfServiceApi.Tests;
public sealed class TestEmailSender:IAppEmailSender { public List<(string To,string Subject,string Body)> Messages {get;}=[]; public Task SendAsync(string to,string subject,string body){Messages.Add((to,subject,body));return Task.CompletedTask;} }