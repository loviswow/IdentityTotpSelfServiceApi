namespace IdentityTotpSelfServiceApi.Entities;
public sealed class RefreshToken
{
    public long Id { get; set; }
    public string UserId { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public string FamilyId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public string? RevokeReason { get; set; }
    // 세션(=family) 표시용. 로그인 시 클라이언트가 보낸 기기 이름과 User-Agent를 회전할 때 그대로 이어받는다.
    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }
    // Refresh rotation 동시 요청을 검출하기 위한 낙관적 동시성 토큰.
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
