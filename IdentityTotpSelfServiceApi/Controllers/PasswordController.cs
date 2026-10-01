using System.Text;
using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
namespace IdentityTotpSelfServiceApi.Controllers;
[ApiController,Route("api/auth"),EnableRateLimiting("auth")]
public sealed class PasswordController(UserManager<ApplicationUser> users,IAppEmailSender mail,RefreshTokenService refresh,AuditService audit):ControllerBase
{
 [HttpPost("forgot-password")]
 public async Task<IActionResult> Forgot(ForgotPasswordRequest req){var u=await users.FindByEmailAsync(req.Email.Trim());
   if(u is not null && await users.IsEmailConfirmedAsync(u)){var t=await users.GeneratePasswordResetTokenAsync(u);var enc=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(t));await mail.SendAsync(u.Email!,"Password reset",$"Reset token: {enc}");}
   return Accepted(new {message="If the account is eligible, reset instructions will be sent."});}
 [HttpPost("reset-password")]
 public async Task<IActionResult> Reset(ResetPasswordRequest req){var u=await users.FindByEmailAsync(req.Email.Trim()); if(u is null)return BadRequest(new{message="Invalid request."});
   string token; try{token=Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Token));}catch{return BadRequest(new{message="Invalid request."});}
   var rs=await users.ResetPasswordAsync(u,token,req.NewPassword);if(!rs.Succeeded)return BadRequest(new{errors=rs.Errors.Select(x=>new{x.Code,x.Description})});
   await refresh.RevokeAllAsync(u.Id,Ip(),"password-reset");await users.UpdateSecurityStampAsync(u);await audit.WriteAsync("password.reset",true,u.Id);return NoContent();}
 [HttpPost("confirm-email")]
 public async Task<IActionResult> Confirm(ConfirmEmailRequest req){var u=await users.FindByIdAsync(req.UserId);if(u is null)return BadRequest();string token;try{token=Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Token));}catch{return BadRequest();}var rs=await users.ConfirmEmailAsync(u,token);return rs.Succeeded?NoContent():BadRequest(new{errors=rs.Errors});}
 [HttpPost("resend-confirmation")]
 public async Task<IActionResult> Resend(ResendConfirmationRequest req){var u=await users.FindByEmailAsync(req.Email.Trim());if(u is not null && !await users.IsEmailConfirmedAsync(u)){var t=await users.GenerateEmailConfirmationTokenAsync(u);var enc=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(t));await mail.SendAsync(u.Email!,"Confirm email",$"Confirmation token: {enc}; userId: {u.Id}");}return Accepted();}
 private string? Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString();
}
