using System.Security.Claims;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace IdentityTotpSelfServiceApi.Controllers;
// 관리자 API. Admin 역할(DB 확인) + MFA 세션만 허용한다(AdminPolicy). 모든 변경 작업은 사유가 필수이며 수행자(ActorUserId)와 함께 감사 로그에 남는다.
[ApiController,Authorize(Policy=AdminPolicy.Name),Route("api/admin")]
public sealed class AdminController(UserManager<ApplicationUser> users,RefreshTokenService refresh,AuditService audit,ApplicationDbContext db):ControllerBase
{
 public const int ReasonMaxLength=500, AuditQueryMaxLimit=500;
 [HttpPost("users/2fa/reset")]
 public async Task<IActionResult> Reset2Fa(AdminReset2FaRequest req){if(Reason(req.Reason) is not {} reason)return ReasonError();var u=await users.FindByIdAsync(req.UserId);if(u is null)return NotFound();
  // 관리자 API로 자기 2FA를 끄면 본인 확인(비밀번호+TOTP) 없이 보안 수준을 낮출 수 있으므로 self-service API를 쓰게 한다.
  if(u.Id==Me())return BadRequest(new{message="Use /api/account/2fa/reset for your own account."});
  var a=await users.SetTwoFactorEnabledAsync(u,false);if(!a.Succeeded)return BadRequest(a.Errors);var b=await users.ResetAuthenticatorKeyAsync(u);if(!b.Succeeded)return BadRequest(b.Errors);await users.UpdateSecurityStampAsync(u);await refresh.RevokeAllAsync(u.Id,Ip(),"admin-2fa-reset");await audit.WriteAsync("admin.2fa.reset",true,u.Id,reason);return NoContent();}
 [HttpGet("users/{userId}/sessions")]
 public async Task<IActionResult> Sessions(string userId){var u=await users.FindByIdAsync(userId);if(u is null)return NotFound();var list=await refresh.ListSessionsAsync(u.Id);await audit.WriteAsync("admin.sessions.read",true,u.Id);return Ok(list.Select(s=>new{s.SessionId,s.DeviceName,s.UserAgent,s.IpAddress,s.CreatedAt,s.LastActiveAt,s.ExpiresAt}));}
 // 침해 대응용 전체 기기 로그아웃. SecurityStamp도 바꿔 sid가 없는 이전 Access Token까지 무효화한다.
 [HttpPost("users/sessions/revoke-all")]
 public async Task<IActionResult> RevokeAllSessions(AdminRevokeSessionsRequest req){if(Reason(req.Reason) is not {} reason)return ReasonError();var u=await users.FindByIdAsync(req.UserId);if(u is null)return NotFound();await users.UpdateSecurityStampAsync(u);await refresh.RevokeAllAsync(u.Id,Ip(),"admin-session-revoke-all");await audit.WriteAsync("admin.sessions.revoke-all",true,u.Id,reason);return NoContent();}
 // 감사 로그 조회. 최신순, limit 기본 100(최대 500). 조회 자체도 감사 로그에 남긴다.
 [HttpGet("audit-logs")]
 public async Task<IActionResult> AuditLogs([FromQuery]string? userId,[FromQuery]string? eventType,[FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,[FromQuery]int limit=100){if(limit is <1 or >AuditQueryMaxLimit)return BadRequest(new{message=$"limit must be between 1 and {AuditQueryMaxLimit}."});
  var q=db.AuditLogs.AsNoTracking().AsQueryable();if(!string.IsNullOrWhiteSpace(userId))q=q.Where(x=>x.UserId==userId);if(!string.IsNullOrWhiteSpace(eventType))q=q.Where(x=>x.EventType==eventType);if(from is not null)q=q.Where(x=>x.OccurredAt>=from);if(to is not null)q=q.Where(x=>x.OccurredAt<to);
  var items=await q.OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.Id).Take(limit).Select(x=>new{x.Id,x.OccurredAt,x.UserId,x.ActorUserId,x.EventType,x.Success,x.IpAddress,x.UserAgent,x.Detail}).ToListAsync();
  await audit.WriteAsync("admin.audit.read",true,Me(),$"userId={userId};eventType={eventType};from={from:O};to={to:O};limit={limit};count={items.Count}");return Ok(items);}
 static string? Reason(string? r){r=r?.Trim();return string.IsNullOrEmpty(r)||r.Length>ReasonMaxLength?null:r;}
 BadRequestObjectResult ReasonError()=>BadRequest(new{message=$"Reason is required (max {ReasonMaxLength} characters)."});
 string? Me()=>User.FindFirstValue(ClaimTypes.NameIdentifier);
 string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();
}
