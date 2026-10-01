using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace IdentityTotpSelfServiceApi.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    JwtTokenService jwt,
    TwoFactorChallengeService challengeService,
    RefreshTokenService refreshTokenService,
    AuditService audit,
    IAppEmailSender mail) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(x => new { x.Code, x.Description }) });

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        await mail.SendAsync(user.Email!, "Confirm email", $"Confirmation token: {encoded}; userId: {user.Id}");
        await audit.WriteAsync("register.success", true, user.Id);
        return Created("", new { user.Id, user.Email, emailConfirmationRequired = true });
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        // 사용자 존재 여부를 외부에 구분해 주지 않는다.
        if (user is null) { await audit.WriteAsync("login.failed", false); return Unauthorized(); }

        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked, new { message = "Account is locked." });

        if (!result.Succeeded) { await audit.WriteAsync("login.failed", false, user.Id); return Unauthorized(); }

        if (user.TwoFactorEnabled)
        {
            return Ok(new LoginResponse(
                true, null, null, jwt.CreateTwoFactorChallenge(user)));
        }

        var access = await jwt.CreateAccessTokenAsync(user);
        var rt = await refreshTokenService.IssueAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { requiresTwoFactor=false, accessToken=access.Token, expiresAt=access.ExpiresAt, refreshToken=rt.Raw, refreshTokenExpiresAt=rt.Entity.ExpiresAt });
    }

    [HttpPost("2fa")]
    public async Task<ActionResult<LoginResponse>> TwoFactor(TwoFactorRequest request)
    {
        var user = await challengeService.ValidateAsync(request.ChallengeToken);
        if (user is null) return Unauthorized();

        var code = NormalizeCode(request.Code);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code);

        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            return Unauthorized(new { message = "Invalid authenticator code." });
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var access = await jwt.CreateAccessTokenAsync(user);
        var rt = await refreshTokenService.IssueAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { requiresTwoFactor=false, accessToken=access.Token, expiresAt=access.ExpiresAt, refreshToken=rt.Raw, refreshTokenExpiresAt=rt.Entity.ExpiresAt });
    }

    [HttpPost("2fa/recovery")]
    public async Task<ActionResult<LoginResponse>> Recovery(RecoveryLoginRequest request)
    {
        var user = await challengeService.ValidateAsync(request.ChallengeToken);
        if (user is null) return Unauthorized();

        var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(
            user, request.RecoveryCode.Trim());

        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid recovery code." });

        await userManager.ResetAccessFailedCountAsync(user);
        var access = await jwt.CreateAccessTokenAsync(user);
        var rt = await refreshTokenService.IssueAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { requiresTwoFactor=false, accessToken=access.Token, expiresAt=access.ExpiresAt, refreshToken=rt.Raw, refreshTokenExpiresAt=rt.Entity.ExpiresAt });
    }

    private static string NormalizeCode(string code)
        => code.Replace(" ", "").Replace("-", "").Trim();
}
