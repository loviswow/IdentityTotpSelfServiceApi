using System.Security.Claims;
using System.Text.Encodings.Web;
using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IdentityTotpSelfServiceApi.Controllers;

[ApiController]
[Authorize]
[Route("api/account/2fa")]
public class TwoFactorController(
    UserManager<ApplicationUser> userManager,
    UrlEncoder urlEncoder,
    IConfiguration config) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<TwoFactorStatusResponse>> Status()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();

        return Ok(new TwoFactorStatusResponse(
            user.TwoFactorEnabled,
            await userManager.CountRecoveryCodesAsync(user)));
    }

    // 아직 2FA가 켜지지 않은 사용자에게 authenticator 등록 정보를 제공한다.
    [HttpPost("setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> Setup()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return Conflict(new { message = "2FA is already enabled." });

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            var reset = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) return IdentityError(reset);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
            return Problem("Unable to create authenticator key.");

        var issuer = config["Totp:Issuer"] ?? "SelfService";
        var account = user.Email ?? user.UserName ?? user.Id;
        var uri = GenerateOtpAuthUri(issuer, account, key);

        // QR 이미지는 Identity 내장 기능이 아니다.
        // 클라이언트는 AuthenticatorUri를 QR로 렌더링하거나 SharedKey를 수동 입력한다.
        return Ok(new TwoFactorSetupResponse(key, uri, false));
    }

    // Authenticator 앱의 첫 TOTP를 검증한 뒤에만 2FA를 활성화한다.
    [HttpPost("enable")]
    public async Task<IActionResult> Enable(TotpCodeRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return Conflict(new { message = "2FA is already enabled." });

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { message = "Run /setup first." });

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));

        if (!valid)
            return BadRequest(new { message = "Invalid authenticator code." });

        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded) return IdentityError(enabled);

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await userManager.UpdateSecurityStampAsync(user);

        return Ok(new
        {
            isTwoFactorEnabled = true,
            recoveryCodes = codes?.ToArray() ?? []
        });
    }

    // 중요 작업: 비밀번호 + 현재 TOTP를 다시 확인한다.
    [HttpPost("disable")]
    public async Task<IActionResult> Disable(Disable2FaRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled)
            return BadRequest(new { message = "2FA is not enabled." });

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized();

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));
        if (!valid) return Unauthorized(new { message = "Invalid authenticator code." });

        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded) return IdentityError(disabled);

        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded) return IdentityError(reset);

        await userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("recovery-codes/regenerate")]
    public async Task<IActionResult> RegenerateRecoveryCodes(TotpCodeRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled)
            return BadRequest(new { message = "2FA is not enabled." });

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));
        if (!valid) return Unauthorized(new { message = "Invalid authenticator code." });

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Ok(new { recoveryCodes = codes?.ToArray() ?? [] });
    }

    // 기기 변경용. 기존 authenticator를 확인한 뒤 키를 교체하고 2FA를 잠시 해제한다.
    // 이후 /setup -> /enable 순서로 새 기기를 등록한다.
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(Reset2FaRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized();

        if (user.TwoFactorEnabled)
        {
            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));
            if (!valid) return Unauthorized(new { message = "Invalid authenticator code." });
        }

        var disable = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disable.Succeeded) return IdentityError(disable);

        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded) return IdentityError(reset);

        await userManager.UpdateSecurityStampAsync(user);
        return Ok(new { message = "Authenticator reset. Run setup and enable again." });
    }

    private async Task<ApplicationUser?> CurrentUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : await userManager.FindByIdAsync(id);
    }

    private string GenerateOtpAuthUri(string issuer, string account, string key)
    {
        return $"otpauth://totp/{urlEncoder.Encode(issuer)}:{urlEncoder.Encode(account)}" +
               $"?secret={key}&issuer={urlEncoder.Encode(issuer)}&digits=6";
    }

    private IActionResult IdentityError(IdentityResult result)
        => BadRequest(new { errors = result.Errors.Select(x => new { x.Code, x.Description }) });

    private static string NormalizeCode(string code)
        => code.Replace(" ", "").Replace("-", "").Trim();
}
