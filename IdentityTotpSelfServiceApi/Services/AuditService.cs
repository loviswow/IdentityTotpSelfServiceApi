using System.Security.Claims;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Entities;
namespace IdentityTotpSelfServiceApi.Services;
public sealed class AuditService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    // userId는 이벤트 대상 사용자, ActorUserId는 요청을 보낸 인증된 사용자다(관리자 작업이면 둘이 다르다).
    // 백그라운드 작업처럼 HttpContext가 없으면 IP·User-Agent·Actor가 비어 있다.
    public async Task WriteAsync(string type,bool success,string? userId=null,string? detail=null)
    {
        var h=accessor.HttpContext;
        db.AuditLogs.Add(new AuditLog { OccurredAt=DateTimeOffset.UtcNow,UserId=userId,EventType=type,Success=success,
          ActorUserId=h?.User.Identity?.IsAuthenticated==true?h.User.FindFirstValue(ClaimTypes.NameIdentifier):null,
          IpAddress=h?.Connection.RemoteIpAddress?.ToString(),UserAgent=Truncate(h?.Request.Headers.UserAgent.ToString(),512),Detail=Truncate(detail,2000) });
        await db.SaveChangesAsync();
    }
    private static string? Truncate(string? s,int max)=>s is null||s.Length<=max?s:s[..max];
}
