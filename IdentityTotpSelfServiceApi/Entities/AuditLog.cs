namespace IdentityTotpSelfServiceApi.Entities;
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? UserId { get; set; }
    // 작업을 수행한 사용자(관리자 작업이면 관리자, 본인 작업이면 본인). 인증되지 않은 요청이면 null.
    public string? ActorUserId { get; set; }
    public string EventType { get; set; } = null!;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
}
