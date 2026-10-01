using IdentityTotpSelfServiceApi.Entities;
using IdentityTotpSelfServiceApi.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace IdentityTotpSelfServiceApi.Data;
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options):IdentityDbContext<ApplicationUser>(options)
{
 public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
 public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  base.OnModelCreating(b);
  var rt=b.Entity<RefreshToken>();
  rt.Property(x=>x.TokenHash).HasMaxLength(64).IsRequired(); rt.Property(x=>x.FamilyId).HasMaxLength(32).IsRequired();
  rt.Property(x=>x.ReplacedByTokenHash).HasMaxLength(64); rt.Property(x=>x.CreatedByIp).HasMaxLength(64); rt.Property(x=>x.RevokedByIp).HasMaxLength(64); rt.Property(x=>x.RevokeReason).HasMaxLength(128); rt.Property(x=>x.Version).HasMaxLength(32).IsRequired().IsConcurrencyToken();
  rt.HasIndex(x=>x.TokenHash).IsUnique(); rt.HasIndex(x=>new{x.UserId,x.FamilyId});
  var al=b.Entity<AuditLog>(); al.Property(x=>x.EventType).HasMaxLength(128).IsRequired(); al.Property(x=>x.IpAddress).HasMaxLength(64); al.Property(x=>x.UserAgent).HasMaxLength(512); al.Property(x=>x.Detail).HasMaxLength(2000); al.HasIndex(x=>x.OccurredAt);
 }
}
