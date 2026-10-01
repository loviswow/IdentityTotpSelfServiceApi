namespace IdentityTotpSelfServiceApi.Entities;
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? UserId { get; set; }
    public string EventType { get; set; } = null!;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
}
