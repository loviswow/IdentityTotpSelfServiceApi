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

    public async Task<(ApplicationUser? User, string? Raw, RefreshToken? NewToken, bool ReuseDetected)> RotateAsync(
        string raw, string? ip, Func<string,Task<ApplicationUser?>> findUser)
    {
        var hash = Hash(raw);
        var old = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (old is null) return (null, null, null, false);
        if (old.RevokedAt is not null)
        {
            await RevokeFamilyAsync(old.FamilyId, ip, "refresh-token-reuse");
            return (null, null, null, true);
        }
        if (old.ExpiresAt <= DateTimeOffset.UtcNow) return (null, null, null, false);

        var user = await findUser(old.UserId);
        if (user is null) return (null, null, null, false);

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
            return (user, next.Raw, next.Entity, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 다른 요청이 먼저 같은 토큰을 회전했다. 현재 tracker를 비우고 family 전체를 폐기한다.
            db.ChangeTracker.Clear();
            await RevokeFamilyAsync(old.FamilyId, ip, "concurrent-refresh-reuse");
            return (null, null, null, true);
        }
    }

    public async Task<bool> RevokeAsync(string raw,string? ip,string reason)
    {
        var token=await db.RefreshTokens.SingleOrDefaultAsync(x=>x.TokenHash==Hash(raw));
        if(token is null || token.RevokedAt is not null) return false;
        token.RevokedAt=DateTimeOffset.UtcNow; token.RevokedByIp=ip; token.RevokeReason=reason; token.Version=Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(); return true;
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
