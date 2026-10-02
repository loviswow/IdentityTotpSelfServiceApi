using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
var builder=WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentityCore<ApplicationUser>(o=>{o.User.RequireUniqueEmail=true;o.SignIn.RequireConfirmedEmail=true;o.Password.RequiredLength=10;o.Password.RequireDigit=true;o.Password.RequireLowercase=true;o.Password.RequireUppercase=true;o.Password.RequireNonAlphanumeric=true;o.Lockout.AllowedForNewUsers=true;o.Lockout.MaxFailedAccessAttempts=5;o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();
_=builder.Configuration["Jwt:Key"]??throw new InvalidOperationException("Jwt:Key missing");
// 검증 키는 옵션 구성 시점에 읽는다. 호스트 빌드 전에 읽으면 이후 추가된 설정(테스트 덮어쓰기 등)과 발급 키가 어긋난다.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.TokenValidationParameters=new TokenValidationParameters{ValidateIssuer=true,ValidIssuer=builder.Configuration["Jwt:Issuer"],ValidateAudience=true,ValidAudience=builder.Configuration["Jwt:Audience"],ValidateLifetime=true,ClockSkew=TimeSpan.FromSeconds(30),ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),NameClaimType=ClaimTypes.Name,RoleClaimType=ClaimTypes.Role};o.Events=new JwtBearerEvents{OnTokenValidated=async ctx=>{if(ctx.Principal?.HasClaim(c=>c.Type=="purpose")==true){ctx.Fail("Not an access token");return;}/* REG-008: 2FA challenge 등 용도 한정 토큰은 Access Token으로 받지 않는다 */var id=ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);var stamp=ctx.Principal?.FindFirstValue("security_stamp");if(id is null||stamp is null){ctx.Fail("Invalid token");return;}var um=ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();var u=await um.FindByIdAsync(id);if(u is null||!string.Equals(u.SecurityStamp,stamp,StringComparison.Ordinal)){ctx.Fail("Token revoked");return;}/* 로그아웃·세션 폐기·재사용 탐지로 끝난 세션(sid)의 Access Token은 만료 전이라도 거부한다 */var sid=ctx.Principal?.FindFirstValue("sid");if(sid is not null&&!await ctx.HttpContext.RequestServices.GetRequiredService<RefreshTokenService>().IsSessionActiveAsync(id,sid))ctx.Fail("Session revoked");}};});
builder.Services.AddAuthorization(o=>o.AddPolicy(AdminPolicy.Name,p=>p.RequireAuthenticatedUser().AddRequirements(new AdminMfaRequirement())));builder.Services.AddScoped<IAuthorizationHandler,AdminMfaHandler>();builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler,AdminAuthorizationResultHandler>();builder.Services.AddHostedService<RefreshTokenCleanupService>();builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<JwtTokenService>();builder.Services.AddScoped<TwoFactorChallengeService>();builder.Services.AddScoped<RefreshTokenService>();builder.Services.AddScoped<AuditService>();
// 메일 발송기는 요청 시점 설정으로 고른다(테스트 설정 덮어쓰기 반영). Email:Smtp:Host가 있으면 SMTP(큐 + 백그라운드 발송), 없으면 Development 환경 + Email:PickupDirectory면 파일 저장, 그 밖에는 로그만 남긴다(실제 발송 없음).
builder.Services.AddOptions<SmtpOptions>().BindConfiguration("Email:Smtp").Validate(o=>!o.Enabled||!string.IsNullOrWhiteSpace(o.FromAddress),"Email:Smtp:FromAddress is required when Email:Smtp:Host is set.").Validate(o=>{try{SmtpEmailSender.ParseSecurity(o.Security);return true;}catch(InvalidOperationException){return false;}},"Email:Smtp:Security must be StartTls, SslOnConnect, Auto or None.").ValidateOnStart();builder.Services.AddSingleton<SmtpEmailSender>();builder.Services.AddSingleton<EmailQueue>();builder.Services.AddHostedService<EmailDispatchService>();
builder.Services.AddSingleton<IAppEmailSender>(sp=>{var cfg=sp.GetRequiredService<IConfiguration>();if(sp.GetRequiredService<IOptions<SmtpOptions>>().Value.Enabled)return sp.GetRequiredService<EmailQueue>();var pickup=cfg["Email:PickupDirectory"];if(sp.GetRequiredService<IHostEnvironment>().IsDevelopment()&&!string.IsNullOrWhiteSpace(pickup))return new PickupDirectoryEmailSender(pickup);return ActivatorUtilities.CreateInstance<DevelopmentEmailSender>(sp);});
builder.Services.AddSingleton(System.Text.Encodings.Web.UrlEncoder.Default);
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("auth",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=builder.Configuration.GetValue<int>("RateLimiting:AuthPermitLimit",10),Window=TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthWindowSeconds",60)),QueueLimit=0,AutoReplenishment=true}));});
builder.Services.AddControllers();builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(c=>{c.SwaggerDoc("v1",new OpenApiInfo{Title="Identity TOTP Self-Service API",Version="v2"});c.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"});c.AddSecurityRequirement(doc=>new OpenApiSecurityRequirement{{new OpenApiSecuritySchemeReference("Bearer",doc),new List<string>()}});});
var app=builder.Build();if(app.Environment.IsDevelopment()){app.UseSwagger();app.UseSwaggerUI();}app.UseHttpsRedirection();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.MapControllers();app.Run();
public partial class Program { }
