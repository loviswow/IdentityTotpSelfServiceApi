using System.Security.Claims;
using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IdentityTotpSelfServiceApi.Controllers;

[ApiController]
[Authorize]
[Route("api/account")]
public class AccountController(UserManager<ApplicationUser> userManager, RefreshTokenService refresh, AuditService audit) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await CurrentUser();
        return user is null ? Unauthorized() : Ok(new { user.Id, user.Email, user.UserName, user.TwoFactorEnabled });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(x => new { x.Code, x.Description }) });

        // ChangePasswordAsync가 SecurityStamp를 갱신하므로 기존 Access JWT는 즉시 거부된다.
        // 기존 Refresh Token도 모두 폐기해 새 Access JWT를 재발급할 우회 경로를 차단한다.
        await refresh.RevokeAllAsync(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString(), "password-change");
        await audit.WriteAsync("password.change", true, user.Id);
        return NoContent();
    }

    private async Task<ApplicationUser?> CurrentUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : await userManager.FindByIdAsync(id);
    }
}
