using System.Security.Claims;
using System.Text.Encodings.Web;
using IdentityTotpSelfServiceApi.Dtos;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace IdentityTotpSelfServiceApi.Controllers;

[ApiController]
[Authorize]
[Route("api/account/2fa")]
public class TwoFactorController(
    UserManager<ApplicationUser> userManager,
    IUserStore<ApplicationUser> userStore,
    UrlEncoder urlEncoder,
    IConfiguration config,
    RefreshTokenService refreshTokenService,
    AuditService audit) : ControllerBase
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
            // ResetAuthenticatorKeyAsync는 SecurityStamp까지 바꿔 지금 쓰는 Access Token을 무효화하므로
            // 바로 이어지는 /enable 호출이 401이 된다. 아직 2FA가 꺼진 상태라 세션을 끊을 이유가 없으므로
            // 키 저장소에 직접 키만 저장한다. 세션 무효화는 /enable에서 일어난다.
            var store = (IUserAuthenticatorKeyStore<ApplicationUser>)userStore;
            await store.SetAuthenticatorKeyAsync(user, userManager.GenerateNewAuthenticatorKey(), HttpContext.RequestAborted);
            var saved = await userManager.UpdateAsync(user);
            if (!saved.Succeeded) return IdentityError(saved);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
            return Problem("Unable to create authenticator key.");

        var uri = AuthenticatorUri(user, key);

        // 클라이언트는 AuthenticatorUri를 QR로 렌더링하거나(/qr로 이미지를 받아도 된다) SharedKey를 수동 입력한다.
        // SharedKey는 TOTP Secret이므로 감사 로그에 남기지 않는다.
        await audit.WriteAsync("2fa.setup", true, user.Id);
        return Ok(new TwoFactorSetupResponse(key, uri, false));
    }

    // /setup으로 만든 등록 정보(AuthenticatorUri)를 QR 이미지로 돌려준다. VB6처럼 QR을 직접 그리기 어려운 클라이언트용.
    // format: png(기본) / bmp(VB6 LoadPicture용) / svg. QR에는 TOTP Secret이 들어 있으므로 캐시하지 않게 하고,
    // 2FA가 이미 켜진 뒤에는 주지 않는다(키를 다시 노출하지 않는다).
    [HttpGet("qr")]
    public async Task<IActionResult> Qr([FromQuery] string? format = "png")
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return Conflict(new { message = "2FA is already enabled." });

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { message = "Run /setup first." });

        var fmt = (format ?? "png").Trim().ToLowerInvariant();
        if (fmt is not ("png" or "bmp" or "svg"))
            return BadRequest(new { message = "format must be png, bmp or svg." });

        using var data = QRCodeGenerator.GenerateQrCode(AuthenticatorUri(user, key), QRCodeGenerator.ECCLevel.M);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return fmt switch
        {
            "bmp" => File(new BitmapByteQRCode(data).GetGraphic(6), "image/bmp"),
            "svg" => File(System.Text.Encoding.UTF8.GetBytes(new SvgQRCode(data).GetGraphic(6)), "image/svg+xml"),
            _ => File(new PngByteQRCode(data).GetGraphic(6), "image/png")
        };
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
        {
            await audit.WriteAsync("2fa.enable.failed", false, user.Id, "code");
            return BadRequest(new { message = "Invalid authenticator code." });
        }

        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded) return IdentityError(enabled);

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await userManager.UpdateSecurityStampAsync(user);
        // 비밀번호만으로 발급된 세션이 Refresh로 amr=mfa Access JWT를 얻지 못하도록 폐기한다(REG-007).
        // 클라이언트는 다시 로그인해 TOTP 2차 인증을 거쳐야 한다.
        await refreshTokenService.RevokeAllAsync(user.Id, Ip(), "2fa-enable");
        await audit.WriteAsync("2fa.enable", true, user.Id);

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
        {
            await audit.WriteAsync("2fa.disable.failed", false, user.Id, "password");
            return Unauthorized();
        }

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));
        if (!valid)
        {
            await audit.WriteAsync("2fa.disable.failed", false, user.Id, "code");
            return Unauthorized(new { message = "Invalid authenticator code." });
        }

        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded) return IdentityError(disabled);

        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded) return IdentityError(reset);

        await userManager.UpdateSecurityStampAsync(user);
        // SecurityStamp만 바꾸면 기존 Refresh Token으로 Access JWT를 다시 받을 수 있으므로 함께 폐기한다(REG-005).
        await refreshTokenService.RevokeAllAsync(user.Id, Ip(), "2fa-disable");
        await audit.WriteAsync("2fa.disable", true, user.Id);
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
        if (!valid)
        {
            await audit.WriteAsync("2fa.recovery-codes.regenerate.failed", false, user.Id, "code");
            return Unauthorized(new { message = "Invalid authenticator code." });
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await audit.WriteAsync("2fa.recovery-codes.regenerate", true, user.Id);
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
        {
            await audit.WriteAsync("2fa.reset.failed", false, user.Id, "password");
            return Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, NormalizeCode(request.Code));
            if (!valid)
            {
                await audit.WriteAsync("2fa.reset.failed", false, user.Id, "code");
                return Unauthorized(new { message = "Invalid authenticator code." });
            }
        }

        var disable = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disable.Succeeded) return IdentityError(disable);

        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded) return IdentityError(reset);

        await userManager.UpdateSecurityStampAsync(user);
        await refreshTokenService.RevokeAllAsync(user.Id, Ip(), "2fa-reset");
        await audit.WriteAsync("2fa.reset", true, user.Id);
        return Ok(new { message = "Authenticator reset. Run setup and enable again." });
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<ApplicationUser?> CurrentUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : await userManager.FindByIdAsync(id);
    }

    private string AuthenticatorUri(ApplicationUser user, string key)
        => GenerateOtpAuthUri(config["Totp:Issuer"] ?? "SelfService", user.Email ?? user.UserName ?? user.Id, key);

    private string GenerateOtpAuthUri(string issuer, string account, string key)
    {
        return $"otpauth://totp/{urlEncoder.Encode(issuer)}:{urlEncoder.Encode(account)}" +
               $"?secret={key}&issuer={urlEncoder.Encode(issuer)}&digits=6";
    }

    private BadRequestObjectResult IdentityError(IdentityResult result)
        => BadRequest(new { errors = result.Errors.Select(x => new { x.Code, x.Description }) });

    private static string NormalizeCode(string code)
        => code.Replace(" ", "").Replace("-", "").Trim();
}
