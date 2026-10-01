using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace IdentityTotpSelfServiceApi.Services;
public sealed class JwtTokenService(IConfiguration config,UserManager<ApplicationUser> users)
{
 public async Task<(string Token,DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(ApplicationUser user){var exp=DateTimeOffset.UtcNow.AddMinutes(config.GetValue<int>("Jwt:AccessTokenMinutes",15));var claims=new List<Claim>{new(JwtRegisteredClaimNames.Sub,user.Id),new(ClaimTypes.NameIdentifier,user.Id),new(ClaimTypes.Name,user.UserName??user.Id),new(JwtRegisteredClaimNames.Email,user.Email??""),new("security_stamp",user.SecurityStamp??""),new("amr",user.TwoFactorEnabled?"mfa":"pwd")};foreach(var role in await users.GetRolesAsync(user))claims.Add(new Claim(ClaimTypes.Role,role));return(Create(claims,exp),exp);}
 public string CreateTwoFactorChallenge(ApplicationUser user){var exp=DateTimeOffset.UtcNow.AddMinutes(config.GetValue<int>("Jwt:TwoFactorChallengeMinutes",5));return Create(new[]{new Claim(JwtRegisteredClaimNames.Sub,user.Id),new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim("purpose","2fa"),new Claim("security_stamp",user.SecurityStamp??"")},exp);}
 private string Create(IEnumerable<Claim> claims,DateTimeOffset exp){var k=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]??throw new InvalidOperationException("Jwt:Key missing")));var jwt=new JwtSecurityToken(config["Jwt:Issuer"],config["Jwt:Audience"],claims,DateTime.UtcNow,exp.UtcDateTime,new SigningCredentials(k,SecurityAlgorithms.HmacSha256));return new JwtSecurityTokenHandler().WriteToken(jwt);}
}
