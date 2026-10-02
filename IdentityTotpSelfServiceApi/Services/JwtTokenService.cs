using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace IdentityTotpSelfServiceApi.Services;
public sealed class JwtTokenService(IConfiguration config,UserManager<ApplicationUser> users)
{
 // sessionId(sid)는 함께 발급한 Refresh Token의 FamilyId다. 세션이 폐기되면 Program.cs의 OnTokenValidated가 이 Access Token을 거부한다.
 public async Task<(string Token,DateTimeOffset ExpiresAt)> CreateAccessTokenAsync(ApplicationUser user,string sessionId){var exp=DateTimeOffset.UtcNow.AddMinutes(config.GetValue<int>("Jwt:AccessTokenMinutes",15));var claims=new List<Claim>{new(JwtRegisteredClaimNames.Sub,user.Id),new(ClaimTypes.NameIdentifier,user.Id),new(ClaimTypes.Name,user.UserName??user.Id),new(JwtRegisteredClaimNames.Email,user.Email??""),new("security_stamp",user.SecurityStamp??""),new("amr",user.TwoFactorEnabled?"mfa":"pwd"),new(JwtRegisteredClaimNames.Sid,sessionId)};foreach(var role in await users.GetRolesAsync(user))claims.Add(new Claim(ClaimTypes.Role,role));return(Create(claims,exp,config["Jwt:Audience"]),exp);}
 // challenge token은 Access Token과 다른 audience로 발급해 Bearer 인증에서 거부되게 한다(REG-008).
 public string CreateTwoFactorChallenge(ApplicationUser user){var exp=DateTimeOffset.UtcNow.AddMinutes(config.GetValue<int>("Jwt:TwoFactorChallengeMinutes",5));return Create(new[]{new Claim(JwtRegisteredClaimNames.Sub,user.Id),new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim("purpose","2fa"),new Claim("security_stamp",user.SecurityStamp??"")},exp,TwoFactorAudience(config));}
 public static string TwoFactorAudience(IConfiguration config)=>config["Jwt:TwoFactorAudience"]??$"{config["Jwt:Audience"]}/2fa";
 private string Create(IEnumerable<Claim> claims,DateTimeOffset exp,string? audience){var k=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]??throw new InvalidOperationException("Jwt:Key missing")));var jwt=new JwtSecurityToken(config["Jwt:Issuer"],audience,claims,DateTime.UtcNow,exp.UtcDateTime,new SigningCredentials(k,SecurityAlgorithms.HmacSha256));return new JwtSecurityTokenHandler().WriteToken(jwt);}
}
