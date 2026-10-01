using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using IdentityTotpSelfServiceApi.Data;
using IdentityTotpSelfServiceApi.Models;
using IdentityTotpSelfServiceApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
var builder=WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentityCore<ApplicationUser>(o=>{o.User.RequireUniqueEmail=true;o.SignIn.RequireConfirmedEmail=true;o.Password.RequiredLength=10;o.Password.RequireDigit=true;o.Password.RequireLowercase=true;o.Password.RequireUppercase=true;o.Password.RequireNonAlphanumeric=true;o.Lockout.AllowedForNewUsers=true;o.Lockout.MaxFailedAccessAttempts=5;o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();
var key=builder.Configuration["Jwt:Key"]??throw new InvalidOperationException("Jwt:Key missing");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.TokenValidationParameters=new TokenValidationParameters{ValidateIssuer=true,ValidIssuer=builder.Configuration["Jwt:Issuer"],ValidateAudience=true,ValidAudience=builder.Configuration["Jwt:Audience"],ValidateLifetime=true,ClockSkew=TimeSpan.FromSeconds(30),ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),NameClaimType=ClaimTypes.Name,RoleClaimType=ClaimTypes.Role};o.Events=new JwtBearerEvents{OnTokenValidated=async ctx=>{var id=ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);var stamp=ctx.Principal?.FindFirstValue("security_stamp");if(id is null||stamp is null){ctx.Fail("Invalid token");return;}var um=ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();var u=await um.FindByIdAsync(id);if(u is null||!string.Equals(u.SecurityStamp,stamp,StringComparison.Ordinal))ctx.Fail("Token revoked");}};});
builder.Services.AddAuthorization();builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<JwtTokenService>();builder.Services.AddScoped<TwoFactorChallengeService>();builder.Services.AddScoped<RefreshTokenService>();builder.Services.AddScoped<AuditService>();builder.Services.AddSingleton<IAppEmailSender,DevelopmentEmailSender>();builder.Services.AddSingleton(System.Text.Encodings.Web.UrlEncoder.Default);
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("auth",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=builder.Configuration.GetValue<int>("RateLimiting:AuthPermitLimit",10),Window=TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthWindowSeconds",60)),QueueLimit=0,AutoReplenishment=true}));});
builder.Services.AddControllers();builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(c=>{c.SwaggerDoc("v1",new OpenApiInfo{Title="Identity TOTP Self-Service API",Version="v2"});c.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"});c.AddSecurityRequirement(new OpenApiSecurityRequirement{{new OpenApiSecurityScheme{Reference=new OpenApiReference{Type=ReferenceType.SecurityScheme,Id="Bearer"}},Array.Empty<string>()}});});
var app=builder.Build();if(app.Environment.IsDevelopment()){app.UseSwagger();app.UseSwaggerUI();}app.UseHttpsRedirection();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.MapControllers();app.Run();
public partial class Program { }
