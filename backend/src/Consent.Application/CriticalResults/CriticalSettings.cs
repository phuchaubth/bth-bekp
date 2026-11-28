using System.Globalization;
using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.CriticalResults;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.CriticalResults;

/// <summary>
/// Thông số vận hành kết quả nguy cấp (quyết định 60): chỉnh khi vận hành, không cần cài bản mới. Hồ sơ chép thời hạn đang áp
/// dụng lúc ghi (<see cref="CriticalDeadlines"/>), nên đổi thông số không làm đổi hạn của hồ sơ đã có.
/// </summary>
/// <param name="NotifyMinutes">Thời hạn báo, tính từ giờ có kết quả (QT-KQNC lần 04 mục 6.2: 10 phút).</param>
/// <param name="ActionMinutes">Thời hạn bác sĩ có hướng xử trí ban đầu, tính từ giờ khoa nhận cuộc gọi (mục 6.4: 10 phút).</param>
/// <param name="WarnMinutes">Còn bấy nhiêu phút thì đồng hồ chuyển màu cam (sắp quá hạn).</param>
/// <param name="PollSeconds">Giao diện tự kiểm tra hồ sơ mới mỗi bấy nhiêu giây (dải cảnh báo, màn hình theo dõi).</param>
/// <param name="SoundRepeatSeconds">Nhắc lại chuông mỗi bấy nhiêu giây khi còn hồ sơ chưa xác nhận; 0 là không nhắc lại.</param>
/// <param name="FlashPeriodMs">Một lần chớp tắt (sáng rồi tối) kéo dài bấy nhiêu mili giây: từ 1 giây trở lên, an toàn cho người nhạy cảm với ánh sáng nhấp nháy.</param>
/// <param name="KeepHours">Hồ sơ hoàn tất, đã hủy còn hiện trên bàn làm việc bấy nhiêu giờ.</param>
/// <param name="LateEntryMinutes">Bước ghi sau giờ sự việc quá bấy nhiêu phút thì gắn nhãn Ghi bù.</param>
/// <param name="RequireAddress">Bắt buộc địa chỉ người bệnh (thông tin định danh thứ ba khi gọi điện, mục 6.2 bước 1).</param>
/// <param name="DisplayFullName">Màn hình theo dõi tại khoa hiện họ tên đầy đủ (mặc định chỉ tên viết tắt).</param>
public sealed record CriticalSettings(
    int NotifyMinutes,
    int ActionMinutes,
    int WarnMinutes,
    int PollSeconds,
    bool SoundEnabled,
    int SoundRepeatSeconds,
    bool FlashEnabled,
    int FlashPeriodMs,
    int KeepHours,
    int LateEntryMinutes,
    bool RequireAddress,
    bool DisplayFullName,
    int TargetNotifyPercent,
    int TargetActionPercent,
    int TargetReadBackPercent,
    int TargetCompletePercent,
    string CodeLaboratory,
    string CodeClinical,
    string CodeMicrobiology)
{
    public const string KeyPrefix = SettingKeys.CriticalPrefix;
    public const int CodeMaxLength = 50;

    public static CriticalSettings Default { get; } = new(
        NotifyMinutes: 10,
        ActionMinutes: 10,
        WarnMinutes: 3,
        PollSeconds: 15,
        SoundEnabled: true,
        SoundRepeatSeconds: 60,
        FlashEnabled: true,
        FlashPeriodMs: 1200,
        KeepHours: 24,
        LateEntryMinutes: 30,
        RequireAddress: true,
        DisplayFullName: false,
        TargetNotifyPercent: 95,
        TargetActionPercent: 90,
        TargetReadBackPercent: 98,
        TargetCompletePercent: 98,
        CodeLaboratory: "QT-KQNC/ST01",
        CodeClinical: "QT-KQNC/ST02",
        CodeMicrobiology: "QT-VS-21/ST02");

    /// <summary>Giới hạn của từng thông số số (tên khóa, nhỏ nhất, lớn nhất, tên hiển thị).</summary>
    internal static IReadOnlyList<(string Key, int Min, int Max, string Name)> Ranges { get; } =
    [
        (nameof(NotifyMinutes), 1, 120, "Thời hạn báo kết quả"),
        (nameof(ActionMinutes), 1, 120, "Thời hạn bác sĩ xử trí ban đầu"),
        (nameof(WarnMinutes), 0, 30, "Báo sắp quá hạn khi còn"),
        (nameof(PollSeconds), 5, 120, "Chu kỳ tự kiểm tra hồ sơ mới"),
        (nameof(SoundRepeatSeconds), 0, 600, "Nhắc lại chuông"),
        (nameof(FlashPeriodMs), 1000, 3000, "Nhịp chớp tắt"),
        (nameof(KeepHours), 1, 168, "Giữ hồ sơ hoàn tất trên bàn làm việc"),
        (nameof(LateEntryMinutes), 5, 1440, "Ngưỡng ghi bù"),
        (nameof(TargetNotifyPercent), 50, 100, "Mục tiêu báo đúng hạn"),
        (nameof(TargetActionPercent), 50, 100, "Mục tiêu bác sĩ xử trí đúng hạn"),
        (nameof(TargetReadBackPercent), 50, 100, "Mục tiêu đọc lại chính xác lần đầu"),
        (nameof(TargetCompletePercent), 50, 100, "Mục tiêu hồ sơ đủ các bước"),
    ];

    public CriticalDeadlines Deadlines => new(NotifyMinutes, ActionMinutes, LateEntryMinutes, RequireAddress);

    /// <summary>Mã số in trên sổ PDF theo mẫu.</summary>
    public string CodeOf(CriticalLogbookForm form) => form switch
    {
        CriticalLogbookForm.Laboratory => CodeLaboratory,
        CriticalLogbookForm.Microbiology => CodeMicrobiology,
        _ => CodeClinical,
    };

    /// <summary>Đọc từ cấu hình hệ thống; khóa thiếu hoặc sai dạng thì dùng mặc định (không chặn làm việc).</summary>
    public static CriticalSettings Read(IEnumerable<SystemSetting> stored)
    {
        var values = stored
            .Where(s => s.Key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            .ToDictionary(s => s.Key[KeyPrefix.Length..], s => s.Value, StringComparer.Ordinal);
        var d = Default;
        int Int(string key, int fallback)
        {
            var range = Ranges.First(r => r.Key == key);
            return values.TryGetValue(key, out var raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value >= range.Min && value <= range.Max
                ? value
                : fallback;
        }

        bool Bool(string key, bool fallback) =>
            values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value) ? value : fallback;

        string Text(string key, string fallback) =>
            values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) && raw.Length <= CodeMaxLength ? raw : fallback;

        return new CriticalSettings(
            Int(nameof(NotifyMinutes), d.NotifyMinutes),
            Int(nameof(ActionMinutes), d.ActionMinutes),
            Int(nameof(WarnMinutes), d.WarnMinutes),
            Int(nameof(PollSeconds), d.PollSeconds),
            Bool(nameof(SoundEnabled), d.SoundEnabled),
            Int(nameof(SoundRepeatSeconds), d.SoundRepeatSeconds),
            Bool(nameof(FlashEnabled), d.FlashEnabled),
            Int(nameof(FlashPeriodMs), d.FlashPeriodMs),
            Int(nameof(KeepHours), d.KeepHours),
            Int(nameof(LateEntryMinutes), d.LateEntryMinutes),
            Bool(nameof(RequireAddress), d.RequireAddress),
            Bool(nameof(DisplayFullName), d.DisplayFullName),
            Int(nameof(TargetNotifyPercent), d.TargetNotifyPercent),
            Int(nameof(TargetActionPercent), d.TargetActionPercent),
            Int(nameof(TargetReadBackPercent), d.TargetReadBackPercent),
            Int(nameof(TargetCompletePercent), d.TargetCompletePercent),
            Text(nameof(CodeLaboratory), d.CodeLaboratory),
            Text(nameof(CodeClinical), d.CodeClinical),
            Text(nameof(CodeMicrobiology), d.CodeMicrobiology));
    }

    /// <summary>Giá trị lưu vào cấu hình hệ thống, theo tên khóa (không có tiền tố).</summary>
    internal IReadOnlyDictionary<string, string> ToValues() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(NotifyMinutes)] = Format(NotifyMinutes),
        [nameof(ActionMinutes)] = Format(ActionMinutes),
        [nameof(WarnMinutes)] = Format(WarnMinutes),
        [nameof(PollSeconds)] = Format(PollSeconds),
        [nameof(SoundEnabled)] = Format(SoundEnabled),
        [nameof(SoundRepeatSeconds)] = Format(SoundRepeatSeconds),
        [nameof(FlashEnabled)] = Format(FlashEnabled),
        [nameof(FlashPeriodMs)] = Format(FlashPeriodMs),
        [nameof(KeepHours)] = Format(KeepHours),
        [nameof(LateEntryMinutes)] = Format(LateEntryMinutes),
        [nameof(RequireAddress)] = Format(RequireAddress),
        [nameof(DisplayFullName)] = Format(DisplayFullName),
        [nameof(TargetNotifyPercent)] = Format(TargetNotifyPercent),
        [nameof(TargetActionPercent)] = Format(TargetActionPercent),
        [nameof(TargetReadBackPercent)] = Format(TargetReadBackPercent),
        [nameof(TargetCompletePercent)] = Format(TargetCompletePercent),
        [nameof(CodeLaboratory)] = CodeLaboratory,
        [nameof(CodeClinical)] = CodeClinical,
        [nameof(CodeMicrobiology)] = CodeMicrobiology,
    };

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(bool value) => value ? "true" : "false";
}

/// <summary>Thông số kèm lần sửa cuối.</summary>
public sealed record CriticalSettingsDto(CriticalSettings Values, DateTimeOffset? UpdatedAtUtc, string? UpdatedBy);

/// <summary>Đọc và sửa thông số vận hành kết quả nguy cấp; mỗi lần sửa ghi nhật ký giá trị cũ và mới.</summary>
public sealed class CriticalSettingsService(
    ISystemSettingRepository settings,
    IUserRepository users,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock)
{
    private const string UpdatedByKey = CriticalSettings.KeyPrefix + "UpdatedBy";

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private static readonly string[] AnyPermission =
    [
        Permissions.CriticalRecord, Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalDisplay,
        Permissions.CriticalMonitor, Permissions.CriticalManage,
    ];

    /// <summary>Thông số đang dùng (không kiểm tra quyền: dùng nội bộ khi ghi hồ sơ, dựng dashboard, sổ).</summary>
    public async Task<CriticalSettings> CurrentAsync(CancellationToken cancellationToken) =>
        CriticalSettings.Read(await settings.GetAllAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Thông số cho giao diện: mọi tài khoản có quyền kết quả nguy cấp (dải cảnh báo, màn hình theo dõi cần nhịp kiểm tra, chuông).</summary>
    public async Task<Result<CriticalSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (!AnyPermission.Any(currentUser.HasPermission))
        {
            return Error.Forbidden("critical.forbidden", "Không có quyền kết quả nguy cấp.");
        }

        return ToDto(await settings.GetAllAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CriticalSettingsDto>> UpdateAsync(CriticalSettings request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!currentUser.HasPermission(Permissions.CriticalManage))
        {
            return Error.Forbidden("critical.forbidden", "Chỉ người quản lý danh mục và cấu hình kết quả nguy cấp được sửa thông số.");
        }

        var cleaned = request with
        {
            CodeLaboratory = request.CodeLaboratory?.Trim() ?? string.Empty,
            CodeClinical = request.CodeClinical?.Trim() ?? string.Empty,
            CodeMicrobiology = request.CodeMicrobiology?.Trim() ?? string.Empty,
        };
        if (Validate(cleaned) is { } error)
        {
            return error;
        }

        var stored = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var before = CriticalSettings.Read(stored).ToValues();
        var after = cleaned.ToValues();
        var changes = after
            .Where(pair => before[pair.Key] != pair.Value)
            .ToDictionary(pair => pair.Key, pair => new[] { before[pair.Key], pair.Value }, StringComparer.Ordinal);
        if (changes.Count == 0)
        {
            return ToDto(stored);
        }

        var now = clock.UtcNow;
        foreach (var (key, value) in after)
        {
            Upsert(stored, CriticalSettings.KeyPrefix + key, value, now);
        }

        var user = currentUser.UserId is { } id ? await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
        var by = user is null ? currentUser.UserName ?? "Không rõ" : $"{user.FullName} ({user.UserName})";
        Upsert(stored, UpdatedByKey, by.Length <= SystemSetting.ValueMaxLength ? by : by[..SystemSetting.ValueMaxLength], now);
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(now, AuditActions.CriticalSettingsUpdated, actor, details: JsonSerializer.Serialize(changes, AuditJson)));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(stored);
    }

    private static Error? Validate(CriticalSettings value)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(CriticalSettings.NotifyMinutes)] = value.NotifyMinutes,
            [nameof(CriticalSettings.ActionMinutes)] = value.ActionMinutes,
            [nameof(CriticalSettings.WarnMinutes)] = value.WarnMinutes,
            [nameof(CriticalSettings.PollSeconds)] = value.PollSeconds,
            [nameof(CriticalSettings.SoundRepeatSeconds)] = value.SoundRepeatSeconds,
            [nameof(CriticalSettings.FlashPeriodMs)] = value.FlashPeriodMs,
            [nameof(CriticalSettings.KeepHours)] = value.KeepHours,
            [nameof(CriticalSettings.LateEntryMinutes)] = value.LateEntryMinutes,
            [nameof(CriticalSettings.TargetNotifyPercent)] = value.TargetNotifyPercent,
            [nameof(CriticalSettings.TargetActionPercent)] = value.TargetActionPercent,
            [nameof(CriticalSettings.TargetReadBackPercent)] = value.TargetReadBackPercent,
            [nameof(CriticalSettings.TargetCompletePercent)] = value.TargetCompletePercent,
        };
        foreach (var (key, min, max, name) in CriticalSettings.Ranges)
        {
            if (numbers[key] < min || numbers[key] > max)
            {
                errors[Camel(key)] = [$"{name}: từ {min} đến {max}."];
            }
        }

        // So với hai thời hạn chỉ khi cả ba thông số đều trong giới hạn (tránh báo lỗi kép cho cùng một chỗ sai).
        string[] deadlineKeys =
            [Camel(nameof(CriticalSettings.NotifyMinutes)), Camel(nameof(CriticalSettings.ActionMinutes)), Camel(nameof(CriticalSettings.WarnMinutes))];
        if (!deadlineKeys.Any(errors.ContainsKey) && value.WarnMinutes >= Math.Min(value.NotifyMinutes, value.ActionMinutes))
        {
            errors[Camel(nameof(CriticalSettings.WarnMinutes))] = ["Báo sắp quá hạn phải ít hơn cả hai thời hạn."];
        }

        foreach (var (key, code) in new[]
                 {
                     (nameof(CriticalSettings.CodeLaboratory), value.CodeLaboratory),
                     (nameof(CriticalSettings.CodeClinical), value.CodeClinical),
                     (nameof(CriticalSettings.CodeMicrobiology), value.CodeMicrobiology),
                 })
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > CriticalSettings.CodeMaxLength || code.Any(char.IsControl))
            {
                errors[Camel(key)] = [$"Mã số sổ: 1 đến {CriticalSettings.CodeMaxLength} ký tự."];
            }
        }

        return errors.Count == 0 ? null : Error.Validation("validation", "Thông số chưa hợp lệ.", errors);
    }

    private static string Camel(string key) => char.ToLowerInvariant(key[0]) + key[1..];

    private static CriticalSettingsDto ToDto(IReadOnlyList<SystemSetting> stored)
    {
        var own = stored.Where(s => s.Key.StartsWith(CriticalSettings.KeyPrefix, StringComparison.Ordinal)).ToList();
        return new CriticalSettingsDto(
            CriticalSettings.Read(stored),
            own.Count == 0 ? null : own.Max(s => s.UpdatedAtUtc),
            own.FirstOrDefault(s => s.Key == UpdatedByKey)?.Value);
    }

    private void Upsert(List<SystemSetting> stored, string key, string value, DateTimeOffset now)
    {
        var existing = stored.FirstOrDefault(s => s.Key == key);
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
}
