using System.Globalization;
using System.Text.Json.Nodes;
using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.ConsentForms;

/// <summary>Kế thừa phiếu lần trước ở màn hình Cấu hình hệ thống: số ngày (0 = tắt), lần đổi gần nhất.</summary>
public sealed record FormInheritanceSettingsDto(int Days, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdateFormInheritanceRequest(int? Days);

/// <summary>Phiếu lần trước dùng để điền sẵn: số phiếu, lúc hoàn tất, câu trả lời đã chuẩn hóa theo phiên bản mẫu hiện hành.</summary>
public sealed record InheritedForm(Guid FormId, string FormNo, DateTimeOffset CompletedAtUtc, JsonObject Data);

/// <summary>
/// Kế thừa phiếu lần trước (quyết định 27): người bệnh làm lại cùng mẫu phiếu ở cùng khoa trong số ngày quản trị đặt thì phiếu
/// mới điền sẵn câu trả lời của phiếu đã hoàn tất gần nhất, để nhập cho nhanh. Chỉ lấy phiếu đã hoàn tất (nhân viên đã kiểm
/// tra), cùng khoa (không lộ nội dung phiếu sang khoa khác). Chỉ kế thừa câu trả lời: ý kiến Đồng ý/Không đồng ý, lời xác
/// nhận và chữ ký luôn làm mới. Thông tin lấy từ hồ sơ người bệnh (trường tự điền) được ưu tiên hơn phiếu cũ.
/// </summary>
public sealed class FormInheritanceService(
    ISystemSettingRepository settings,
    IConsentFormRepository forms,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Số ngày tối đa đặt được (một năm).</summary>
    public const int MaxDays = 365;

    /// <summary>Số ngày đang đặt; chưa đặt, sai định dạng hoặc 0 là tắt.</summary>
    public async Task<int> GetDaysAsync(CancellationToken cancellationToken) =>
        Parse(await FindSettingAsync(cancellationToken).ConfigureAwait(false));

    public async Task<FormInheritanceSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await FindSettingAsync(cancellationToken).ConfigureAwait(false);
        return new FormInheritanceSettingsDto(Parse(setting), setting?.UpdatedAtUtc);
    }

    /// <summary>Đặt số ngày, có hiệu lực ngay cho phiếu lập sau đó; thay đổi được ghi nhật ký như mọi cấu hình khác.</summary>
    public async Task<Result<FormInheritanceSettingsDto>> UpdateAsync(UpdateFormInheritanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Days is not { } days || days < 0 || days > MaxDays)
        {
            return Error.ValidationField("days", $"Số ngày từ 0 (tắt) đến {MaxDays}.");
        }

        var value = days.ToString(CultureInfo.InvariantCulture);
        var existing = await FindSettingAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            settings.Add(SystemSetting.Create(SettingKeys.FormInheritanceDays, value, clock.UtcNow));
        }
        else if (existing.Value != value)
        {
            existing.Update(value, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phiếu lần trước để kế thừa: phiếu đã hoàn tất gần nhất của người bệnh theo mẫu, ở khoa, hoàn tất trong
    /// <paramref name="days"/> ngày gần đây. Câu trả lời được chuẩn hóa theo <paramref name="definition"/> (phiên bản mẫu
    /// đang dùng): trường không còn trong mẫu hay giá trị không còn hợp lệ bị bỏ. Tắt (0 ngày) hoặc không có thì trả về null.
    /// </summary>
    public async Task<InheritedForm?> FindAsync(
        Guid patientId, Guid templateId, Guid departmentId, TemplateDefinition definition, int days, CancellationToken cancellationToken)
    {
        if (days <= 0)
        {
            return null;
        }

        var previous = await forms.FindLatestCompletedAsync(
            patientId, templateId, departmentId, clock.UtcNow.AddDays(-days), cancellationToken).ConfigureAwait(false);
        if (previous?.CompletedAtUtc is not { } completedAt)
        {
            return null;
        }

        var data = FormDataEvaluator.Evaluate(definition, FormDataEvaluator.ParseData(previous.DataJson), keepHidden: true).Data;
        return new InheritedForm(previous.Id, previous.FormNo, completedAt, data);
    }

    /// <summary>Câu trả lời kế thừa, rồi giá trị có trong hồ sơ người bệnh (trường tự điền) đè lên.</summary>
    public static JsonObject Merge(JsonObject inherited, JsonObject prefill)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        ArgumentNullException.ThrowIfNull(prefill);
        var data = inherited.DeepClone().AsObject();
        foreach (var (key, value) in prefill)
        {
            if (value is not null)
            {
                data[key] = value.DeepClone();
            }
        }

        return data;
    }

    /// <summary>Các trường có câu trả lời trong phiếu lần trước (để kiosk bỏ qua, không gửi giá trị).</summary>
    public static IReadOnlyList<string> AnsweredKeys(InheritedForm inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        return [.. inherited.Data
            .Where(p => p.Value is not null && !(p.Value is JsonValue v && v.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)))
            .Select(p => p.Key)];
    }

    /// <summary>Lưu ý hiện cho nhân viên khi phiếu mới được điền sẵn theo phiếu cũ.</summary>
    public static string Notice(InheritedForm inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        var completed = inherited.CompletedAtUtc.ToOffset(ConsentFormService.VietnamOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return $"Đã điền sẵn theo phiếu {inherited.FormNo} (hoàn tất {completed}): kiểm tra lại trước khi trình ký.";
    }

    private static int Parse(SystemSetting? setting) =>
        int.TryParse(setting?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is > 0 and <= MaxDays ? days : 0;

    private async Task<SystemSetting?> FindSettingAsync(CancellationToken cancellationToken) =>
        (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => s.Key == SettingKeys.FormInheritanceDays);
}
