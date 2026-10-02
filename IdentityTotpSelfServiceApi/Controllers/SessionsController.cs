using System.Security.Claims;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IdentityTotpSelfServiceApi.Controllers;

// 로그인 기기(세션) 조회와 강제 로그아웃. 세션 = Refresh Token family, sessionId = FamilyId.
// 현재 세션은 Access Token의 sid 클레임으로 판단한다.
[ApiController]
[Authorize]
[Route("api/account/sessions")]
public class SessionsController(UserManager<ApplicationUser> userManager, RefreshTokenService refresh, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var sessions = await refresh.ListSessionsAsync(UserId());
        var current = CurrentSessionId();
        return Ok(sessions.Select(s => new
        {
            s.SessionId, s.DeviceName, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastActiveAt, s.ExpiresAt,
            current = s.SessionId == current
        }));
    }

    // 특정 기기 로그아웃. 현재 세션을 지정하면 자기 로그아웃과 같다.
    // 다른 사용자의 세션이거나 이미 끝난 세션이면 존재 여부를 구분하지 않고 404.
    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> Revoke(string sessionId)
    {
        if (!await refresh.RevokeSessionAsync(UserId(), sessionId, Ip(), "session-revoke"))
            return NotFound();
        await audit.WriteAsync("session.revoke", true, UserId(), sessionId);
        return NoContent();
    }

    // 현재 기기만 남기고 다른 기기를 모두 로그아웃한다.
    [HttpPost("revoke-others")]
    public async Task<IActionResult> RevokeOthers()
    {
        var current = CurrentSessionId();
        if (current is null)
            return BadRequest(new { message = "The current access token has no session. Sign in again." });
        var revoked = await refresh.RevokeAllAsync(UserId(), Ip(), "session-revoke-others", exceptSessionId: current);
        await audit.WriteAsync("session.revoke-others", true, UserId(), $"revokedSessions={revoked}");
        return Ok(new { revokedSessions = revoked });
    }

    // 현재 기기를 포함한 전체 기기 로그아웃. SecurityStamp도 바꿔 sid가 없는 이전 Access Token까지 무효화한다.
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        var user = await userManager.FindByIdAsync(UserId());
        if (user is null) return Unauthorized();
        await userManager.UpdateSecurityStampAsync(user);
        var revoked = await refresh.RevokeAllAsync(user.Id, Ip(), "session-revoke-all");
        await audit.WriteAsync("session.revoke-all", true, user.Id, $"revokedSessions={revoked}");
        return NoContent();
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private string? CurrentSessionId() => User.FindFirstValue("sid");

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
