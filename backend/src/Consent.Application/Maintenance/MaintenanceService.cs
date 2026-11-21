using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.Settings;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.Maintenance;

/// <summary>
/// Trạng thái bảo trì cho mọi người, không cần đăng nhập: giao diện hiện trang "Hệ thống đang bảo trì" với lý do và giờ dự kiến
/// xong. Không có ai bật, lúc nào bật.
/// </summary>
public sealed record MaintenanceStatusDto(bool Enabled, string? Reason, DateTimeOffset? ExpectedEndAtUtc);

/// <summary>Trạng thái bảo trì cho người có quyền bảo trì (trang Bảo trì hệ thống): thêm thời điểm bật và người bật.</summary>
public sealed record MaintenanceSettingsDto(
    bool Enabled,
    string? Reason,
    DateTimeOffset? ExpectedEndAtUtc,
    DateTimeOffset? StartedAtUtc,
    string? StartedBy);

/// <param name="Enabled">Bật (hoặc đang bật thì sửa lý do, giờ dự kiến xong) hay tắt bảo trì.</param>
/// <param name="Reason">Lý do hiện cho người dùng; bắt buộc khi bật, tối đa <see cref="MaintenanceService.ReasonMaxLength"/> ký tự.</param>
/// <param name="ExpectedEndAtUtc">Giờ dự kiến xong, chỉ để hiển thị (hết giờ không tự tắt); để trống khi chưa rõ.</param>
public sealed record UpdateMaintenanceRequest(bool Enabled, string? Reason, DateTimeOffset? ExpectedEndAtUtc);

/// <summary>Trạng thái bảo trì đọc từ cấu hình hệ thống.</summary>
public sealed record MaintenanceState(
    bool Enabled,
    string? Reason,
    DateTimeOffset? ExpectedEndAtUtc,
    DateTimeOffset? StartedAtUtc,
    string? StartedBy)
{
    public static MaintenanceState Off { get; } = new(false, null, null, null, null);
}

public sealed class UpdateMaintenanceRequestValidator : AbstractValidator<UpdateMaintenanceRequest>
{
    public UpdateMaintenanceRequestValidator(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        When(x => x.Enabled, () =>
        {
            RuleFor(x => MaintenanceService.CleanReason(x.Reason))
                .NotEmpty().WithMessage("Vui lòng nhập lý do bảo trì (hiện cho người dùng).")
                .MaximumLength(MaintenanceService.ReasonMaxLength)
                .WithMessage($"Lý do bảo trì tối đa {MaintenanceService.ReasonMaxLength} ký tự.")
                .OverridePropertyName(nameof(UpdateMaintenanceRequest.Reason));
            RuleFor(x => x.ExpectedEndAtUtc)
                .Must(end => end > clock.UtcNow).WithMessage("Giờ dự kiến xong phải sau thời điểm hiện tại.")
                .Must(end => end <= clock.UtcNow.AddDays(MaintenanceService.MaxExpectedDays))
                .WithMessage($"Giờ dự kiến xong phải trong vòng {MaintenanceService.MaxExpectedDays} ngày tới.")
                .When(x => x.ExpectedEndAtUtc is not null);
        });
    }
}

/// <summary>
/// Chế độ bảo trì (quyết định 53). Người có quyền <see cref="Permissions.SystemMaintenance"/> (quản trị hệ thống) bật trên giao
/// diện, ghi lý do và giờ dự kiến xong. Đang bảo trì thì người không có quyền này (kể cả kiosk, người bệnh ký qua mã QR) không
/// đăng nhập, không làm mới phiên, không thao tác được (API trả 503); người có quyền vẫn đăng nhập và thao tác như thường để điều
/// chỉnh cấu hình, mẫu phiếu, dữ liệu. Phiên đăng nhập của người dùng không bị thu hồi: tắt bảo trì là họ làm tiếp.
/// Trạng thái lưu ở cấu hình hệ thống (<c>Maintenance.*</c>); tắt khẩn cấp bằng SQL thì xóa các dòng đó.
/// </summary>
public sealed class MaintenanceService(
    ISystemSettingRepository settings,
    IUserRepository users,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    MaintenanceStateCache cache,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock,
    IValidator<UpdateMaintenanceRequest> validator)
{
    public const int ReasonMaxLength = 200;

    /// <summary>Mã lỗi khi bị chặn vì đang bảo trì (giao diện dựa vào mã này để hiện trang bảo trì).</summary>
    public const string ErrorCode = "system.maintenance";

    /// <summary>Giờ dự kiến xong tối đa bao nhiêu ngày tính từ lúc lưu (tránh gõ nhầm năm).</summary>
    public const int MaxExpectedDays = 30;

    private const string KeyPrefix = "Maintenance.";

    /// <summary>Như nhật ký tự động: giữ nguyên tiếng Việt có dấu trong chi tiết để đọc được thẳng trong CSDL.</summary>
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    /// <summary>Người có các quyền này được vào hệ thống khi đang bảo trì.</summary>
    public static bool CanEnterDuringMaintenance(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Contains(Permissions.SystemMaintenance, StringComparer.Ordinal);
    }

    /// <summary>Lỗi 503 trả cho người không có quyền bảo trì, kèm lý do và giờ dự kiến xong để giao diện hiện.</summary>
    public static Error Blocked(MaintenanceState state, string message)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Error.Unavailable(ErrorCode, message) with
        {
            Metadata = new Dictionary<string, object?>
            {
                ["reason"] = state.Reason,
                ["expectedEndAtUtc"] = state.ExpectedEndAtUtc,
            },
        };
    }

    /// <summary>
    /// Trạng thái hiện tại; giữ trong bộ nhớ vài giây (<see cref="MaintenanceStateCache"/>) vì mọi yêu cầu tới API đều hỏi.
    /// </summary>
    public async Task<MaintenanceState> GetStateAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (cache.TryGet(now, out var cached))
        {
            return cached;
        }

        var state = Read(await settings.GetAllAsync(cancellationToken).ConfigureAwait(false));
        cache.Set(state, now);
        return state;
    }

    public async Task<MaintenanceStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return new MaintenanceStatusDto(state.Enabled, state.Reason, state.ExpectedEndAtUtc);
    }

    /// <summary>Trạng thái cho trang Bảo trì hệ thống, đọc thẳng từ CSDL (thấy ngay cả thay đổi bằng SQL).</summary>
    public async Task<MaintenanceSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var state = Read(await settings.GetAllAsync(cancellationToken).ConfigureAwait(false));
        cache.Set(state, clock.UtcNow);
        return ToSettings(state);
    }

    /// <summary>
    /// Bật, sửa (đang bật thì lưu lý do, giờ dự kiến mới; giữ thời điểm và người bật) hoặc tắt bảo trì; có hiệu lực ngay. Tắt khi
    /// đang không bảo trì thì không làm gì.
    /// </summary>
    public async Task<Result<MaintenanceSettingsDto>> UpdateAsync(UpdateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var stored = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var current = Read(stored);
        if (!request.Enabled)
        {
            if (!current.Enabled)
            {
                cache.Set(current, now);
                return ToSettings(current);
            }

            foreach (var setting in stored.Where(s => s.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)))
            {
                settings.Remove(setting);
            }

            AddAudit(AuditActions.MaintenanceDisabled, new
            {
                reason = current.Reason,
                startedAtUtc = current.StartedAtUtc,
                startedBy = current.StartedBy,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            cache.Set(MaintenanceState.Off, now);
            return ToSettings(MaintenanceState.Off);
        }

        var reason = CleanReason(request.Reason);
        var expectedEnd = request.ExpectedEndAtUtc?.ToUniversalTime();
        if (current.Enabled && current.Reason == reason && current.ExpectedEndAtUtc == expectedEnd)
        {
            cache.Set(current, now);
            return ToSettings(current);
        }

        var next = current.Enabled
            ? current with { Reason = reason, ExpectedEndAtUtc = expectedEnd, StartedAtUtc = current.StartedAtUtc ?? now }
            : new MaintenanceState(true, reason, expectedEnd, now, await DescribeCurrentUserAsync(cancellationToken).ConfigureAwait(false));
        Upsert(stored, SettingKeys.MaintenanceReason, next.Reason, now);
        Upsert(stored, SettingKeys.MaintenanceExpectedEndAtUtc, Format(next.ExpectedEndAtUtc), now);
        Upsert(stored, SettingKeys.MaintenanceStartedAtUtc, Format(next.StartedAtUtc), now);
        Upsert(stored, SettingKeys.MaintenanceStartedBy, next.StartedBy, now);
        AddAudit(
            current.Enabled ? AuditActions.MaintenanceUpdated : AuditActions.MaintenanceEnabled,
            new { reason = next.Reason, expectedEndAtUtc = next.ExpectedEndAtUtc });
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cache.Set(next, now);
        return ToSettings(next);
    }

    /// <summary>Lý do một dòng: ký tự điều khiển (xuống dòng…) thành khoảng trắng, gộp khoảng trắng liền nhau, bỏ hai đầu.</summary>
    internal static string CleanReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(reason.Length);
        foreach (var c in reason)
        {
            var space = char.IsWhiteSpace(c) || char.IsControl(c);
            if (!space)
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Có dòng <see cref="SettingKeys.MaintenanceReason"/> là đang bảo trì; các dòng khác chỉ là thông tin thêm (thiếu hoặc sai
    /// dạng thì coi như không có).
    /// </summary>
    internal static MaintenanceState Read(IEnumerable<SystemSetting> stored)
    {
        var values = stored
            .Where(s => s.Key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (!values.TryGetValue(SettingKeys.MaintenanceReason, out var reason))
        {
            return MaintenanceState.Off;
        }

        return new MaintenanceState(
            true,
            reason,
            ParseTime(values.GetValueOrDefault(SettingKeys.MaintenanceExpectedEndAtUtc)),
            ParseTime(values.GetValueOrDefault(SettingKeys.MaintenanceStartedAtUtc)),
            values.GetValueOrDefault(SettingKeys.MaintenanceStartedBy));
    }

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.ToUniversalTime()
            : null;

    private static string? Format(DateTimeOffset? time) => time?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static MaintenanceSettingsDto ToSettings(MaintenanceState state) =>
        new(state.Enabled, state.Reason, state.ExpectedEndAtUtc, state.StartedAtUtc, state.StartedBy);

    /// <summary>Thêm, sửa hoặc (giá trị trống) xóa một dòng cấu hình.</summary>
    private void Upsert(List<SystemSetting> stored, string key, string? value, DateTimeOffset now)
    {
        var existing = stored.FirstOrDefault(s => s.Key == key);
        if (string.IsNullOrEmpty(value))
        {
            if (existing is not null)
            {
                settings.Remove(existing);
                stored.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            var created = SystemSetting.Create(key, value, now);
            settings.Add(created);
            stored.Add(created);
        }
        else if (existing.Value != value)
        {
            existing.Update(value, now);
        }
    }

    /// <summary>"Họ tên (tên đăng nhập)" của người đang bật bảo trì.</summary>
    private async Task<string?> DescribeCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } id ? await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return user is null ? currentUser.UserName : $"{user.FullName} ({user.UserName})";
    }

    private void AddAudit(string action, object details)
    {
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(clock.UtcNow, action, actor, details: JsonSerializer.Serialize(details, AuditJson)));
    }
}

/// <summary>
/// Trạng thái bảo trì giữ trong bộ nhớ (dùng chung cho mọi yêu cầu), để không phải đọc CSDL ở mỗi yêu cầu. Bật/tắt trên giao
/// diện thì cập nhật ngay; sửa thẳng trong CSDL (tắt khẩn cấp bằng SQL) thì có hiệu lực sau tối đa <see cref="Lifetime"/>.
/// </summary>
public sealed class MaintenanceStateCache(TimeSpan lifetime)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(5);

    private Entry? _entry;

    public TimeSpan Lifetime { get; } = lifetime;

    public bool TryGet(DateTimeOffset nowUtc, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MaintenanceState? state)
    {
        var entry = Volatile.Read(ref _entry);
        if (entry is not null && nowUtc >= entry.LoadedAtUtc && nowUtc - entry.LoadedAtUtc < Lifetime)
        {
            state = entry.State;
            return true;
        }

        state = null;
        return false;
    }

    public void Set(MaintenanceState state, DateTimeOffset nowUtc) => Volatile.Write(ref _entry, new Entry(state, nowUtc));

    private sealed record Entry(MaintenanceState State, DateTimeOffset LoadedAtUtc);
}
