using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Entities;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.EntityFrameworkCore;

namespace IdentityTotpSelfServiceApi.Services;

// 세션 목록 항목. 세션 = Refresh Token family. SessionId는 FamilyId이며 토큰이나 해시가 아니다.
public sealed record SessionInfo(string SessionId, string? DeviceName, string? UserAgent, string? IpAddress,
    DateTimeOffset CreatedAt, DateTimeOffset LastActiveAt, DateTimeOffset ExpiresAt);

public sealed class RefreshTokenService(ApplicationDbContext db, IConfiguration config)
{
    public const int DeviceNameMaxLength = 128, UserAgentMaxLength = 512;

    // deviceName은 클라이언트가 로그인 시 보낸 표시용 이름, userAgent는 요청 헤더. 둘 다 길이를 잘라 저장한다.
    public async Task<(string Raw, RefreshToken Entity)> IssueAsync(ApplicationUser user, string? ip, string? deviceName = null, string? userAgent = null)
    {
        var pair = Create(user, ip, null, Truncate(deviceName, DeviceNameMaxLength), Truncate(userAgent, UserAgentMaxLength));
        db.RefreshTokens.Add(pair.Entity);
        await db.SaveChangesAsync();
        return pair;
    }

    // 실패 사유(Failure)
    //   reuse   : 이미 회전된 토큰의 재사용 또는 동시 회전 충돌. 탈취 의심 → family 전체 폐기.
    //   revoked : 로그아웃·비밀번호 변경·2FA 변경·관리자 초기화·세션 폐기 등으로 정상 폐기된 토큰. FailureDetail = 폐기 사유.
    //   expired / unknown / user-missing
    public async Task<(ApplicationUser? User, string? Raw, RefreshToken? NewToken, string? Failure, string? FailureDetail, string? OwnerId)> RotateAsync(
        string raw, string? ip, Func<string,Task<ApplicationUser?>> findUser)
    {
        var hash = Hash(raw);
        var old = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (old is null) return (null, null, null, "unknown", null, null);
        if (old.RevokedAt is not null)
        {
            // 회전으로 대체된 토큰만 재사용 공격으로 본다(REG-010). 다른 사유로 폐기된 토큰은 family에 활성 토큰이 남아 있지 않다.
            if (old.RevokeReason != "rotated") return (null, null, null, "revoked", old.RevokeReason, old.UserId);
            await RevokeFamilyAsync(old.FamilyId, ip, "refresh-token-reuse");
            return (null, null, null, "reuse", null, old.UserId);
        }
        if (old.ExpiresAt <= DateTimeOffset.UtcNow) return (null, null, null, "expired", null, old.UserId);

        var user = await findUser(old.UserId);
        if (user is null) return (null, null, null, "user-missing", null, old.UserId);

        // 새 토큰 추가와 기존 토큰 폐기를 하나의 SaveChanges로 커밋한다.
        // Version concurrency token 때문에 동일 refresh token의 동시 회전은 하나만 성공한다.
        // 세션 표시 정보(기기 이름, User-Agent)는 family 안에서 그대로 이어받는다.
        var next = Create(user, ip, old.FamilyId, old.DeviceName, old.UserAgent);
        db.RefreshTokens.Add(next.Entity);
        old.RevokedAt = DateTimeOffset.UtcNow;
        old.RevokedByIp = ip;
        old.RevokeReason = "rotated";
        old.ReplacedByTokenHash = next.Entity.TokenHash;
        old.Version = Guid.NewGuid().ToString("N");

        try
        {
            await db.SaveChangesAsync();
            return (user, next.Raw, next.Entity, null, null, old.UserId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 다른 요청이 먼저 같은 토큰을 회전했다. 현재 tracker를 비우고 family 전체를 폐기한다.
            db.ChangeTracker.Clear();
            await RevokeFamilyAsync(old.FamilyId, ip, "concurrent-refresh-reuse");
            return (null, null, null, "reuse", "concurrent", old.UserId);
        }
    }

    // 로그아웃. 제출한 토큰의 세션(family)에 남은 활성 토큰을 모두 폐기한다(보통 그 토큰 하나).
    // 같은 세션이 그 사이 회전되었으면 새 토큰도 함께 폐기된다(REG-013). 반환값은 토큰 소유자 UserId, 없거나 이미 폐기된 토큰이면 null.
    public async Task<string?> RevokeAsync(string raw,string? ip,string reason)
    {
        var hash=Hash(raw);
        var token=await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash);
        if(token is null || token.RevokedAt is not null) return null;
        await RevokeActiveAsync(x=>x.FamilyId==token.FamilyId,ip,reason);
        return token.UserId;
    }

    // 사용자의 활성 토큰을 모두 폐기하고 폐기한 세션(family) 수를 반환한다. exceptSessionId가 있으면 그 세션은 남긴다.
    // 폐기 사유에 "rotated"를 쓰면 재사용 공격으로 오인되므로 쓰지 않는다(REG-010).
    public async Task<int> RevokeAllAsync(string userId,string? ip,string reason,string? exceptSessionId=null)
        => (await RevokeActiveAsync(x=>x.UserId==userId && (exceptSessionId==null || x.FamilyId!=exceptSessionId),ip,reason)).Count;

    // 본인 세션 하나를 폐기한다. 다른 사용자의 세션이거나 활성 토큰이 없으면 false.
    public async Task<bool> RevokeSessionAsync(string userId,string sessionId,string? ip,string reason)
        => (await RevokeActiveAsync(x=>x.UserId==userId && x.FamilyId==sessionId,ip,reason)).Count>0;

    // Access Token의 sid가 가리키는 세션에 아직 쓸 수 있는 Refresh Token이 남아 있는지 확인한다.
    // 로그아웃·세션 폐기·재사용 탐지로 family가 폐기되면 그 세션의 Access Token도 만료 전에 거부된다.
    public Task<bool> IsSessionActiveAsync(string userId,string sessionId)
    {
        var now=DateTimeOffset.UtcNow;
        return db.RefreshTokens.AnyAsync(x=>x.UserId==userId && x.FamilyId==sessionId && x.RevokedAt==null && x.ExpiresAt>now);
    }

    // 활성 세션 목록. 세션마다 활성 토큰은 하나이며(회전 시 이전 토큰 폐기), 시작 시각은 family의 첫 토큰 발급 시각이다.
    public async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string userId)
    {
        var now=DateTimeOffset.UtcNow;
        var active=await db.RefreshTokens.AsNoTracking().Where(x=>x.UserId==userId && x.RevokedAt==null && x.ExpiresAt>now).ToListAsync();
        if(active.Count==0) return [];
        var families=active.Select(x=>x.FamilyId).Distinct().ToList();
        var starts=await db.RefreshTokens.AsNoTracking().Where(x=>x.UserId==userId && families.Contains(x.FamilyId))
            .GroupBy(x=>x.FamilyId).Select(g=>new{g.Key,Start=g.Min(x=>x.CreatedAt)}).ToDictionaryAsync(x=>x.Key,x=>x.Start);
        return active.GroupBy(x=>x.FamilyId).Select(g=>g.OrderByDescending(x=>x.CreatedAt).First())
            .Select(x=>new SessionInfo(x.FamilyId,x.DeviceName,x.UserAgent,x.CreatedByIp,starts.GetValueOrDefault(x.FamilyId,x.CreatedAt),x.CreatedAt,x.ExpiresAt))
            .OrderByDescending(x=>x.LastActiveAt).ToList();
    }

    // 세션(family)의 모든 토큰이 cutoff 이전에 만료된 경우에만 그 family의 토큰을 batchSize씩 지우고, 지운 개수를 반환한다.
    // 살아 있는 family의 회전된 토큰을 지우면 그 토큰이 다시 들어왔을 때 재사용 탐지(family 폐기)가 동작하지 않으므로 남긴다.
    // 다른 인스턴스가 같은 행을 먼저 지워 동시성 충돌이 나면 추적을 풀고 다시 조회한다.
    public async Task<int> PurgeExpiredAsync(DateTimeOffset cutoff,int batchSize=500,CancellationToken ct=default)
    {
        var total=0;
        for(var conflicts=0;;)
        {
            var batch=await db.RefreshTokens.Where(x=>x.ExpiresAt<cutoff && !db.RefreshTokens.Any(y=>y.FamilyId==x.FamilyId && y.ExpiresAt>=cutoff))
                .OrderBy(x=>x.Id).Take(batchSize).ToListAsync(ct);
            if(batch.Count==0) return total;
            db.RefreshTokens.RemoveRange(batch);
            try { await db.SaveChangesAsync(ct); total+=batch.Count; }
            catch(DbUpdateConcurrencyException) when (++conflicts<=10) { }
            finally { DetachRefreshTokens(); }
        }
    }

    private Task RevokeFamilyAsync(string family,string? ip,string reason)=>RevokeActiveAsync(x=>x.FamilyId==family,ip,reason);

    // filter에 맞는 활성 토큰(폐기·만료되지 않은)을 폐기하고 폐기한 FamilyId 목록을 반환한다.
    // 읽은 뒤 저장하기 전에 다른 요청이 같은 토큰을 회전하면 Version 충돌이 난다. 이때 다시 읽으면 회전으로 생긴 새 토큰도
    // 대상에 들어오므로 재시도한다. 충돌을 그대로 던지면 폐기가 통째로 롤백되어 공격자의 토큰이 살아남는다(REG-013).
    // ChangeTracker.Clear()는 같은 컨텍스트의 사용자 엔티티까지 떼어 내므로 RefreshToken 추적만 푼다.
    private async Task<List<string>> RevokeActiveAsync(Expression<Func<RefreshToken,bool>> filter,string? ip,string reason)
    {
        for(var attempt=1;;attempt++)
        {
            var now=DateTimeOffset.UtcNow;
            var list=await db.RefreshTokens.Where(x=>x.RevokedAt==null && x.ExpiresAt>now).Where(filter).ToListAsync();
            foreach(var x in list){x.RevokedAt=now;x.RevokedByIp=ip;x.RevokeReason=reason;x.Version=Guid.NewGuid().ToString("N");}
            try { await db.SaveChangesAsync(); return list.Select(x=>x.FamilyId).Distinct().ToList(); }
            catch(DbUpdateConcurrencyException) when (attempt<5) { DetachRefreshTokens(); }
        }
    }

    private void DetachRefreshTokens()
    {
        foreach(var e in db.ChangeTracker.Entries<RefreshToken>().ToList()) e.State=EntityState.Detached;
    }

    private (string Raw, RefreshToken Entity) Create(ApplicationUser user,string? ip,string? familyId,string? deviceName,string? userAgent)
    {
        var raw=Base64Url(RandomNumberGenerator.GetBytes(64));
        return (raw,new RefreshToken{UserId=user.Id,TokenHash=Hash(raw),FamilyId=familyId??Guid.NewGuid().ToString("N"),CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=DateTimeOffset.UtcNow.AddDays(config.GetValue<int>("Jwt:RefreshTokenDays",14)),CreatedByIp=ip,DeviceName=deviceName,UserAgent=userAgent,Version=Guid.NewGuid().ToString("N")});
    }

    private static string? Truncate(string? s,int max){s=s?.Trim();return string.IsNullOrEmpty(s)?null:s.Length<=max?s:s[..max];}
    public static string Hash(string raw)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static string Base64Url(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
}
