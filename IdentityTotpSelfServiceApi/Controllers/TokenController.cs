using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace IdentityTotpSelfServiceApi.Controllers;
[ApiController,Route("api/auth/token"),EnableRateLimiting("auth")]
public sealed class TokenController(RefreshTokenService refresh,JwtTokenService jwt,UserManager<ApplicationUser> users,AuditService audit):ControllerBase
{
 [HttpPost("refresh")]
 public async Task<IActionResult> Refresh(RefreshRequest req){ var r=await refresh.RotateAsync(req.RefreshToken,Ip(),id=>users.FindByIdAsync(id));
   if(r.User is null){var type=r.Failure switch{"reuse"=>"refresh.reuse","revoked"=>"refresh.revoked",_=>"refresh.failed"};await audit.WriteAsync(type,false,r.OwnerId,r.Failure is "revoked" or "reuse"?r.FailureDetail:r.Failure);return Unauthorized();}
   var access=await jwt.CreateAccessTokenAsync(r.User,r.NewToken!.FamilyId); await audit.WriteAsync("refresh.success",true,r.User.Id);
   return Ok(new TokenPairResponse(access.Token,access.ExpiresAt,r.Raw!,r.NewToken!.ExpiresAt)); }
 [HttpPost("revoke")]
 public async Task<IActionResult> Revoke(RevokeRequest req){var userId=await refresh.RevokeAsync(req.RefreshToken,Ip(),"user-revoke");await audit.WriteAsync(userId is null?"logout.failed":"logout",userId is not null,userId);return NoContent();}
 private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();
}
