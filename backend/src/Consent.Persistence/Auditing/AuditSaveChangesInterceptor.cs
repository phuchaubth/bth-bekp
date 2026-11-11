using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Consent.Application.Abstractions;
using Consent.Domain.Auditing;
using System.Reflection;
using Consent.Domain.Common;
using Consent.Domain.Identity;
using Consent.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Consent.Persistence.Auditing;

/// <summary>
/// Tự động ghi nhật ký audit cho mọi thao tác tạo/sửa/xóa trên thực thể <see cref="IAuditableEntity"/>,
/// trong cùng giao dịch với thay đổi dữ liệu. Giá trị nhạy cảm (mật khẩu đã băm) được che.
/// </summary>
internal sealed class AuditSaveChangesInterceptor(
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock) : SaveChangesInterceptor
{
    public const string MaskedValue = "***";

    /// <summary>Thuộc tính kỹ thuật, thay đổi thường xuyên hoặc dẫn xuất; không cần ghi vào nhật ký.</summary>
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        ConfigurationExtensions.RowVersion,
        PatientConfiguration.CitizenIdHash,
        nameof(User.FailedLoginCount),
        nameof(User.LockoutEndUtc),
        nameof(User.LastLoginAtUtc),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Giữ nguyên tiếng Việt có dấu trong JSON để dễ đọc; vẫn mã hóa ký tự nguy hiểm với HTML.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditableEntity
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var actor = new AuditActor(
            currentUser.UserId,
            currentUser.IsAuthenticated ? currentUser.UserName : AuditActor.System.UserName,
            requestContext.IpAddress,
            requestContext.UserAgent,
            requestContext.CorrelationId);

        foreach (var entry in entries)
        {
            var (action, details) = entry.State switch
            {
                EntityState.Added => (AuditActions.Create, Snapshot(entry, useOriginalValues: false)),
                EntityState.Deleted => (AuditActions.Delete, Snapshot(entry, useOriginalValues: true)),
                _ => (AuditActions.Update, ModifiedValues(entry)),
            };

            if (details is null)
            {
                continue;
            }

            context.Set<AuditLog>().Add(AuditLog.Create(
                now,
                action,
                actor,
                entry.Metadata.ClrType.Name,
                PrimaryKey(entry),
                JsonSerializer.Serialize(details, JsonOptions)));
        }
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, bool useOriginalValues) =>
        entry.Properties
            .Where(p => !IgnoredProperties.Contains(p.Metadata.Name))
            .ToDictionary(
                p => p.Metadata.Name,
                p => MaskIfSensitive(p, useOriginalValues ? p.OriginalValue : p.CurrentValue));

    private static object? MaskIfSensitive(PropertyEntry property, object? value) =>
        value is not null && IsSensitive(property) ? MaskedValue : value;

    /// <summary>Thuộc tính gắn <see cref="SensitiveDataAttribute"/> (số CCCD, số điện thoại, mã BHYT, mật khẩu đã băm…).</summary>
    private static bool IsSensitive(PropertyEntry property) =>
        property.Metadata.PropertyInfo?.GetCustomAttribute<SensitiveDataAttribute>() is not null;

    /// <summary>Chỉ các thuộc tính thực sự thay đổi; trả về <c>null</c> nếu chỉ thay đổi thuộc tính kỹ thuật.</summary>
    private static Dictionary<string, object?>? ModifiedValues(EntityEntry entry)
    {
        var changes = entry.Properties
            .Where(p => p.IsModified
                        && !IgnoredProperties.Contains(p.Metadata.Name)
                        && !Equals(p.OriginalValue, p.CurrentValue))
            .ToDictionary(
                p => p.Metadata.Name,
                p => (object?)new { old = MaskIfSensitive(p, p.OriginalValue), @new = MaskIfSensitive(p, p.CurrentValue) });
        return changes.Count == 0 ? null : changes;
    }

    private static string PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return string.Empty;
        }

        return string.Join(
            ':',
            key.Properties.Select(p => Convert.ToString(entry.Property(p.Name).CurrentValue, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
