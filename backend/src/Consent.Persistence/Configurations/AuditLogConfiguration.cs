using Consent.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consent.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public const string TableName = "AuditLogs";

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(AuditLog.ActionMaxLength).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(AuditLog.EntityTypeMaxLength);
        builder.Property(a => a.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength);
        builder.Property(a => a.UserName).HasMaxLength(AuditLog.UserNameMaxLength);
        builder.Property(a => a.IpAddress).HasMaxLength(AuditLog.IpAddressMaxLength);
        builder.Property(a => a.UserAgent).HasMaxLength(AuditLog.UserAgentMaxLength);
        builder.Property(a => a.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength);
        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
    }
}
