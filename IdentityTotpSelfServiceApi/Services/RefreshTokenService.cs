using System.Security.Cryptography;
using System.Text;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Entities;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.EntityFrameworkCore;

namespace IdentityTotpSelfServiceApi.Services;

public sealed class RefreshTokenService(ApplicationDbContext db, IConfiguration config)
{
    public async Task<(string Raw, RefreshToken Entity)> IssueAsync(ApplicationUser user, string? ip, string? familyId = null)
    {
        var pair = Create(user, ip, familyId);
        db.RefreshTokens.Add(pair.Entity);
        await db.SaveChangesAsync();
        return pair;
    }

    // 실패 사유(Failure)
    //   reuse   : 이미 회전된 토큰의 재사용 또는 동시 회전 충돌. 탈취 의심 → family 전체 폐기.
    //   revoked : 로그아웃·비밀번호 변경·2FA 변경·관리자 초기화 등으로 정상 폐기된 토큰. FailureDetail = 폐기 사유.
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
        var next = Create(user, ip, old.FamilyId);
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

    // 폐기한 토큰의 UserId를 반환한다. 없는 토큰이거나 이미 폐기된 토큰이면 null.
    public async Task<string?> RevokeAsync(string raw,string? ip,string reason)
    {
        var token=await db.RefreshTokens.SingleOrDefaultAsync(x=>x.TokenHash==Hash(raw));
        if(token is null || token.RevokedAt is not null) return null;
        token.RevokedAt=DateTimeOffset.UtcNow; token.RevokedByIp=ip; token.RevokeReason=reason; token.Version=Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(); return token.UserId;
    }

    public async Task RevokeAllAsync(string userId,string? ip,string reason)
    {
        var list=await db.RefreshTokens.Where(x=>x.UserId==userId && x.RevokedAt==null).ToListAsync();
        foreach(var x in list){x.RevokedAt=DateTimeOffset.UtcNow;x.RevokedByIp=ip;x.RevokeReason=reason;x.Version=Guid.NewGuid().ToString("N");}
        await db.SaveChangesAsync();
    }

    private async Task RevokeFamilyAsync(string family,string? ip,string reason)
    {
        var list=await db.RefreshTokens.Where(x=>x.FamilyId==family && x.RevokedAt==null).ToListAsync();
        foreach(var x in list){x.RevokedAt=DateTimeOffset.UtcNow;x.RevokedByIp=ip;x.RevokeReason=reason;x.Version=Guid.NewGuid().ToString("N");}
        await db.SaveChangesAsync();
    }

    private (string Raw, RefreshToken Entity) Create(ApplicationUser user,string? ip,string? familyId)
    {
        var raw=Base64Url(RandomNumberGenerator.GetBytes(64));
        return (raw,new RefreshToken{UserId=user.Id,TokenHash=Hash(raw),FamilyId=familyId??Guid.NewGuid().ToString("N"),CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=DateTimeOffset.UtcNow.AddDays(config.GetValue<int>("Jwt:RefreshTokenDays",14)),CreatedByIp=ip,Version=Guid.NewGuid().ToString("N")});
    }

    public static string Hash(string raw)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static string Base64Url(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
}
