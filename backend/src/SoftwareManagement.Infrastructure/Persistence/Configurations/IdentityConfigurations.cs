using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoftwareManagement.Domain.Audit;
using SoftwareManagement.Domain.Identity;
using SoftwareManagement.Domain.Settings;

namespace SoftwareManagement.Infrastructure.Persistence.Configurations;

public sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property(u => u.FullName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.DisplayTimeZone).HasMaxLength(60).IsRequired();
        builder.Property(u => u.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(u => u.ModifiedBy).HasMaxLength(256);
        builder.HasIndex(u => u.IsActive);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Permissions");
        builder.Property(p => p.Name).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(60).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
        builder.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RefreshTokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(t => t.CreatedByIp).HasMaxLength(45).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(200);
        builder.Property(t => t.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(256);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAtUtc });
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("LoginAttempts");
        builder.Property(a => a.EmailAttempted).HasMaxLength(256).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(a => a.UserAgent).HasMaxLength(400);
        builder.Property(a => a.FailureReason).HasMaxLength(120);
        builder.Property(a => a.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(256);
        builder.Property(a => a.RowVersion).IsRowVersion();

        // Serves the lockout window query in BR-IAM-02 and the Auditor's failure view.
        builder.HasIndex(a => new { a.EmailAttempted, a.CreatedAtUtc });
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.ActorEmail).HasMaxLength(256);
        builder.Property(a => a.ActorIp).HasMaxLength(45);
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAtUtc });
        builder.HasIndex(a => a.OccurredAtUtc);
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("SystemSettings");
        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Category).HasMaxLength(60).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasIndex(s => s.Key).IsUnique();
    }
}
