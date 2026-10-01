using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace IdentityTotpSelfServiceApi.Services;

public sealed class TwoFactorChallengeService(
    IConfiguration config,
    UserManager<ApplicationUser> userManager)
{
    public async Task<ApplicationUser?> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing.");
            var handler = new JwtSecurityTokenHandler();

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = JwtTokenService.TwoFactorAudience(config),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(15),
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            }, out _);

            if (principal.FindFirstValue("purpose") != "2fa") return null;

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId is null) return null;

            var user = await userManager.FindByIdAsync(userId);
            if (user is null || !user.TwoFactorEnabled) return null;

            var stamp = principal.FindFirstValue("security_stamp");
            if (!string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
                return null;

            return user;
        }
        catch
        {
            return null;
        }
    }
}
