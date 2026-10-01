using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Entities;
namespace IdentityTotpSelfServiceApi.Services;
public sealed class AuditService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    public async Task WriteAsync(string type,bool success,string? userId=null,string? detail=null)
    {
        var h=accessor.HttpContext;
        db.AuditLogs.Add(new AuditLog { OccurredAt=DateTimeOffset.UtcNow,UserId=userId,EventType=type,Success=success,
          IpAddress=h?.Connection.RemoteIpAddress?.ToString(),UserAgent=h?.Request.Headers.UserAgent.ToString(),Detail=detail });
        await db.SaveChangesAsync();
    }
}
