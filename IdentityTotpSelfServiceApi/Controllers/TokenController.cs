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
   if(r.User is null){await audit.WriteAsync(r.ReuseDetected?"refresh.reuse":"refresh.failed",false);return Unauthorized();}
   var access=await jwt.CreateAccessTokenAsync(r.User); await audit.WriteAsync("refresh.success",true,r.User.Id);
   return Ok(new TokenPairResponse(access.Token,access.ExpiresAt,r.Raw!,r.NewToken!.ExpiresAt)); }
 [HttpPost("revoke")]
 public async Task<IActionResult> Revoke(RevokeRequest req){await refresh.RevokeAsync(req.RefreshToken,Ip(),"user-revoke");return NoContent();}
 private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();
}
