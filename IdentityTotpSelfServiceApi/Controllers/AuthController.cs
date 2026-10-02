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

        var wasLockedOut = await userManager.IsLockedOutAsync(user);
        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            // 이번 실패로 잠긴 경우와 이미 잠긴 계정에 대한 시도를 구분해 기록한다.
            await audit.WriteAsync(wasLockedOut ? "login.locked" : "account.locked", false, user.Id);
            return StatusCode(StatusCodes.Status423Locked, new { message = "Account is locked." });
        }

        if (!result.Succeeded) { await audit.WriteAsync("login.failed", false, user.Id); return Unauthorized(); }

        if (user.TwoFactorEnabled)
        {
            await audit.WriteAsync("login.2fa-required", true, user.Id);
            return Ok(new LoginResponse(
                true, null, null, jwt.CreateTwoFactorChallenge(user)));
        }

        var tokens = await IssueTokensAsync(user, request.DeviceName);
        await audit.WriteAsync("login.success", true, user.Id);
        return Ok(tokens);
    }

    [HttpPost("2fa")]
    public async Task<ActionResult<LoginResponse>> TwoFactor(TwoFactorRequest request)
    {
        var user = await challengeService.ValidateAsync(request.ChallengeToken);
        if (user is null) return Unauthorized();

        // 빈 코드는 입력 누락이므로 실패 횟수에 넣지 않고 400으로 돌려준다(REG-014).
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "Authenticator code is required." });

        // 2차 인증 실패도 로그인 실패와 같은 lockout 카운트를 사용한다(REG-006).
        if (await userManager.IsLockedOutAsync(user))
        {
            await audit.WriteAsync("2fa.locked", false, user.Id);
            return StatusCode(StatusCodes.Status423Locked, new { message = "Account is locked." });
        }

        var code = NormalizeCode(request.Code);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code);

        if (!valid)
        {
            await RecordSecondFactorFailureAsync(user, "2fa.failed");
            return Unauthorized(new { message = "Invalid authenticator code." });
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var tokens = await IssueTokensAsync(user, request.DeviceName);
        await audit.WriteAsync("2fa.success", true, user.Id);
        return Ok(tokens);
    }

    [HttpPost("2fa/recovery")]
    public async Task<ActionResult<LoginResponse>> Recovery(RecoveryLoginRequest request)
    {
        var user = await challengeService.ValidateAsync(request.ChallengeToken);
        if (user is null) return Unauthorized();

        // 공백 코드를 그대로 넘기면 Identity가 ArgumentException을 던져 500이 된다(REG-014).
        if (string.IsNullOrWhiteSpace(request.RecoveryCode))
            return BadRequest(new { message = "Recovery code is required." });

        if (await userManager.IsLockedOutAsync(user))
        {
            await audit.WriteAsync("recovery.locked", false, user.Id);
            return StatusCode(StatusCodes.Status423Locked, new { message = "Account is locked." });
        }

        var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(
            user, request.RecoveryCode.Trim());

        if (!result.Succeeded)
        {
            await RecordSecondFactorFailureAsync(user, "recovery.failed");
            return Unauthorized(new { message = "Invalid recovery code." });
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var tokens = await IssueTokensAsync(user, request.DeviceName);
        // 복구 코드 사용은 Authenticator 분실 가능성을 뜻하므로 남은 개수를 함께 기록한다(코드 원문은 기록하지 않는다).
        await audit.WriteAsync("recovery.success", true, user.Id, $"recoveryCodesLeft={await userManager.CountRecoveryCodesAsync(user)}");
        return Ok(tokens);
    }

    // Refresh Token(=새 세션)을 먼저 만들고, 그 FamilyId를 sid로 담은 Access Token을 발급한다.
    private async Task<object> IssueTokensAsync(ApplicationUser user, string? deviceName)
    {
        var rt = await refreshTokenService.IssueAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString(), deviceName, Request.Headers.UserAgent.ToString());
        var access = await jwt.CreateAccessTokenAsync(user, rt.Entity.FamilyId);
        return new { requiresTwoFactor=false, accessToken=access.Token, expiresAt=access.ExpiresAt, refreshToken=rt.Raw, refreshTokenExpiresAt=rt.Entity.ExpiresAt };
    }

    private async Task RecordSecondFactorFailureAsync(ApplicationUser user, string eventType)
    {
        await userManager.AccessFailedAsync(user);
        await audit.WriteAsync(eventType, false, user.Id);
        if (await userManager.IsLockedOutAsync(user))
            await audit.WriteAsync("account.locked", false, user.Id);
    }

    private static string NormalizeCode(string code)
        => code.Replace(" ", "").Replace("-", "").Trim();
}
