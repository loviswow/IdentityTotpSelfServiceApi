using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace IdentityTotpSelfServiceApi.Controllers;
[ApiController,Authorize(Roles="Admin"),Route("api/admin/users")]
public sealed class AdminController(UserManager<ApplicationUser> users,RefreshTokenService refresh,AuditService audit):ControllerBase
{
 [HttpPost("2fa/reset")]
 public async Task<IActionResult> Reset2Fa(AdminReset2FaRequest req){if(string.IsNullOrWhiteSpace(req.Reason))return BadRequest(new{message="Reason is required."});var u=await users.FindByIdAsync(req.UserId);if(u is null)return NotFound();
  var a=await users.SetTwoFactorEnabledAsync(u,false);if(!a.Succeeded)return BadRequest(a.Errors);var b=await users.ResetAuthenticatorKeyAsync(u);if(!b.Succeeded)return BadRequest(b.Errors);await users.UpdateSecurityStampAsync(u);await refresh.RevokeAllAsync(u.Id,HttpContext.Connection.RemoteIpAddress?.ToString(),"admin-2fa-reset");await audit.WriteAsync("admin.2fa.reset",true,u.Id,req.Reason);return NoContent();}
}
