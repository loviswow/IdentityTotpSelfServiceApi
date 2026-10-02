using System.Security.Claims;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;

namespace IdentityTotpSelfServiceApi.Services;

// 관리자 API 정책. [Authorize(Policy = AdminPolicy.Name)]로 사용한다.
// 1. Admin 역할은 토큰 클레임이 아니라 DB에서 확인한다. 역할을 회수하면 이미 발급된 토큰도 즉시 거부된다.
// 2. 2차 인증(TOTP/복구 코드)을 거친 세션(amr=mfa)이어야 하고, 지금도 2FA가 켜져 있어야 한다.
public static class AdminPolicy
{
    public const string Name = "AdminMfa";
    public const string NotAdmin = "not-admin", MfaRequired = "mfa-required";
}

public sealed class AdminMfaRequirement : IAuthorizationRequirement;

public sealed class AdminMfaHandler(UserManager<ApplicationUser> users) : AuthorizationHandler<AdminMfaRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminMfaRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null || !await users.IsInRoleAsync(user, "Admin"))
        {
            context.Fail(new AuthorizationFailureReason(this, AdminPolicy.NotAdmin));
            return;
        }
        if (context.User.FindFirstValue("amr") != "mfa" || !user.TwoFactorEnabled)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminPolicy.MfaRequired));
            return;
        }
        context.Succeed(requirement);
    }
}

// 관리자 API 접근 거부(403)를 감사 로그(admin.denied)에 남기고, 거부 사유를 응답 본문으로 알려 준다.
// 그 밖의 결과(401, 일반 API의 403, 성공)는 기본 처리기에 맡긴다.
public sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && policy.Requirements.OfType<AdminMfaRequirement>().Any())
        {
            var reason = authorizeResult.AuthorizationFailure?.FailureReasons.Select(r => r.Message).FirstOrDefault() ?? AdminPolicy.NotAdmin;
            var audit = context.RequestServices.GetRequiredService<AuditService>();
            await audit.WriteAsync("admin.denied", false, context.User.FindFirstValue(ClaimTypes.NameIdentifier), reason);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = reason == AdminPolicy.MfaRequired
                    ? "Admin API requires a session signed in with two-factor authentication."
                    : "Admin role required.",
                reason
            });
            return;
        }
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
