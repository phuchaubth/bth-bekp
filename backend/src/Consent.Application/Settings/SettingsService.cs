using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Domain.Settings;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.Settings;

/// <summary>Tên đơn vị in trên PDF (màn hình 19).</summary>
public sealed record HospitalSettingsDto(string ParentAgency, string Name, string Place, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdateHospitalSettingsRequest(string? ParentAgency, string? Name, string? Place);

/// <summary>Logo bệnh viện: ảnh PNG/JPG dạng Base64 (có thể kèm tiền tố <c>data:image/...;base64,</c>), tối đa 1 MB.</summary>
public sealed record UploadLogoRequest(string? Image);

/// <summary>
/// Ảnh khung chữ ký: ảnh PNG/JPG dạng Base64 (như logo), tối đa 1 MB. Nền thiết kế của bệnh viện quanh khung ký trên màn hình;
/// không in vào ảnh chữ ký hay PDF.
/// </summary>
public sealed record UploadSignatureFrameRequest(string? Image);

/// <summary>Ảnh đang dùng (logo hoặc khung chữ ký; không có thì null).</summary>
public sealed record LogoInfoDto(string ContentType, int Width, int Height, DateTimeOffset UpdatedAtUtc);

/// <summary>Cấu hình hệ thống đang có hiệu lực (phần sửa được và phần chỉ đọc lấy từ cấu hình triển khai).</summary>
/// <param name="HisEnabled">Tra cứu HIS trực tiếp ngay khi quét CCCD (đang tạm hoãn, quyết định 9).</param>
/// <param name="PatientInfoEnabled">
/// Đã cấu hình API thông tin người bệnh (quyết định 22): nút Lấy thông tin, lượt tiếp nhận, số liệu điều dưỡng.
/// </param>
public sealed record SystemSettingsDto(
    HospitalSettingsDto Hospital,
    LogoInfoDto? Logo,
    bool HisEnabled,
    bool SmartCaEnabled,
    int ExportMaxRows,
    string RetentionPolicy,
    LogoInfoDto? SignatureFrame = null,
    bool PatientInfoEnabled = false);

public sealed class UpdateHospitalSettingsRequestValidator : AbstractValidator<UpdateHospitalSettingsRequest>
{
    public UpdateHospitalSettingsRequestValidator()
    {
        RuleFor(x => x.ParentAgency).NotEmpty().WithMessage("Vui lòng nhập cơ quan chủ quản.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên bệnh viện.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
        RuleFor(x => x.Place).NotEmpty().WithMessage("Vui lòng nhập địa danh.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
    }
}

/// <summary>
/// Cấu hình do quản trị sửa trên giao diện, lưu trong CSDL. Giá trị chưa đặt lấy từ cấu hình triển khai (`Hospital:*`).
/// Không có bí mật nào ở đây.
/// </summary>
public sealed class SettingsService(
    ISystemSettingRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    IHisGateway his,
    IOptions<HospitalOptions> hospitalDefaults,
    IValidator<UpdateHospitalSettingsRequest> validator,
    IDocumentStore documents,
    IPatientSource patientSource)
{
    public const string RetentionPolicy = "Theo thời hạn lưu trữ hồ sơ bệnh án; hệ thống không tự động xóa phiếu.";

    /// <summary>Tên đơn vị để in phiếu: giá trị trong CSDL, không có thì dùng cấu hình triển khai.</summary>
    public async Task<HospitalOptions> ResolveHospitalAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var defaults = hospitalDefaults.Value;
        string Get(string key, string fallback) => stored.FirstOrDefault(s => s.Key == key)?.Value ?? fallback;
        return new HospitalOptions
        {
            ParentAgency = Get(SettingKeys.HospitalParentAgency, defaults.ParentAgency),
            Name = Get(SettingKeys.HospitalName, defaults.Name),
            Place = Get(SettingKeys.HospitalPlace, defaults.Place),
        };
    }

    public async Task<SystemSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var hospital = await ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var updatedAt = stored.Where(s => s.Key.StartsWith("Hospital.", StringComparison.Ordinal))
            .Select(s => (DateTimeOffset?)s.UpdatedAtUtc).DefaultIfEmpty(null).Max();
        return new SystemSettingsDto(
            new HospitalSettingsDto(hospital.ParentAgency, hospital.Name, hospital.Place, updatedAt),
            await ImageInfoAsync(stored, SettingKeys.LogoStorageKey, cancellationToken).ConfigureAwait(false),
            his.IsEnabled,
            SmartCaEnabled: false,
            Exports.ConsentFormExportService.MaxRows,
            RetentionPolicy,
            await ImageInfoAsync(stored, SettingKeys.SignatureFrameStorageKey, cancellationToken).ConfigureAwait(false),
            patientSource.IsEnabled);
    }

    /// <summary>
    /// Khung chữ ký đang dùng (kích thước để màn hình ký giữ đúng tỉ lệ ảnh), hoặc null nếu chưa có. Ai cũng xem được, kể cả
    /// điện thoại đang ở chế độ ký và kiosk: đây là ảnh nhận diện của bệnh viện, không có dữ liệu người bệnh.
    /// </summary>
    public async Task<LogoInfoDto?> GetSignatureFrameInfoAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return await ImageInfoAsync(stored, SettingKeys.SignatureFrameStorageKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ảnh khung chữ ký; chưa có, hoặc file mất/hỏng, thì trả về null (khung ký hiện như mặc định).</summary>
    public Task<LogoImage?> ResolveSignatureFrameAsync(CancellationToken cancellationToken) =>
        ResolveImageAsync(SettingKeys.SignatureFrameStorageKey, cancellationToken);

    private async Task<LogoInfoDto?> ImageInfoAsync(
        IReadOnlyCollection<SystemSetting> stored, string storageKey, CancellationToken cancellationToken)
    {
        if (stored.FirstOrDefault(s => s.Key == storageKey)?.UpdatedAtUtc is not { } updatedAt)
        {
            return null;
        }

        var image = await ReadImageAsync(stored, storageKey, cancellationToken).ConfigureAwait(false);
        return image is null ? null : new LogoInfoDto(image.ContentType, image.Width, image.Height, updatedAt);
    }

    public async Task<Result<SystemSettingsDto>> UpdateHospitalAsync(UpdateHospitalSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var stored = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var now = clock.UtcNow;
        Upsert(SettingKeys.HospitalParentAgency, request.ParentAgency!);
        Upsert(SettingKeys.HospitalName, request.Name!);
        Upsert(SettingKeys.HospitalPlace, request.Place!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);

        void Upsert(string key, string value) => UpsertSetting(stored, key, value, now);
    }

    /// <summary>
    /// Logo in ở đầu phiếu: đọc từ kho tài liệu theo khóa đã lưu. Chưa có logo, hoặc file mất/hỏng, thì trả về null (phiếu
    /// vẫn in được, không có logo).
    /// </summary>
    public Task<LogoImage?> ResolveLogoAsync(CancellationToken cancellationToken) =>
        ResolveImageAsync(SettingKeys.LogoStorageKey, cancellationToken);

    /// <summary>Tải lên hoặc thay logo. Áp dụng cho PDF tạo sau khi lưu; PDF đã ký giữ nguyên.</summary>
    public async Task<Result<SystemSettingsDto>> UploadLogoAsync(UploadLogoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var image = LogoImage.Parse(request.Image);
        return image.IsFailure
            ? image.Error!
            : await SaveImageAsync(image.Value, "logo", SettingKeys.LogoStorageKey, SettingKeys.LogoContentType, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Bỏ logo: phiếu in sau đó không có logo.</summary>
    public Task<SystemSettingsDto> DeleteLogoAsync(CancellationToken cancellationToken) =>
        DeleteImageAsync(SettingKeys.LogoStorageKey, SettingKeys.LogoContentType, cancellationToken);

    /// <summary>
    /// Tải lên hoặc thay ảnh khung chữ ký (PNG). Có hiệu lực ngay ở mọi màn hình ký mở sau đó và ở PDF tạo sau khi lưu (mỗi
    /// chỗ ký in khung, chưa ký thì khung trống); PDF đã ký giữ nguyên. Phải là PNG đọc được để in lên PDF.
    /// </summary>
    public async Task<Result<SystemSettingsDto>> UploadSignatureFrameAsync(
        UploadSignatureFrameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var image = LogoImage.Parse(request.Image, "ảnh khung chữ ký", pngOnly: true);
        if (image.IsFailure)
        {
            return image.Error!;
        }

        if (SignatureFramePrint.Prepare(image.Value.Content) is null)
        {
            return Error.ValidationField(
                "image",
                "Ảnh khung chữ ký dùng kiểu PNG chưa đọc được (cần PNG 8 bit, không xen kẽ). Mở ảnh bằng Paint rồi lưu lại dạng PNG.");
        }

        return await SaveImageAsync(
            image.Value, "khung-chu-ky", SettingKeys.SignatureFrameStorageKey, SettingKeys.SignatureFrameContentType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Bỏ ảnh khung chữ ký: khung ký trở lại dạng mặc định (nền trắng, viền nét đứt).</summary>
    public Task<SystemSettingsDto> DeleteSignatureFrameAsync(CancellationToken cancellationToken) =>
        DeleteImageAsync(SettingKeys.SignatureFrameStorageKey, SettingKeys.SignatureFrameContentType, cancellationToken);

    private async Task<LogoImage?> ResolveImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return await ReadImageAsync(stored, storageKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LogoImage?> ReadImageAsync(
        IReadOnlyCollection<SystemSetting> stored, string storageKey, CancellationToken cancellationToken)
    {
        if (stored.FirstOrDefault(s => s.Key == storageKey)?.Value is not { } key)
        {
            return null;
        }

        try
        {
            return LogoImage.Read(await documents.ReadAsync(key, cancellationToken).ConfigureAwait(false));
        }
        catch (DocumentUnavailableException)
        {
            return null;
        }
    }

    /// <summary>Lưu ảnh đã kiểm tra vào kho tài liệu và ghi khóa, kiểu ảnh vào cấu hình.</summary>
    private async Task<SystemSettingsDto> SaveImageAsync(
        LogoImage image, string fileStem, string storageKey, string contentTypeKey, CancellationToken cancellationToken)
    {
        var extension = image.ContentType == LogoImage.Png ? "png" : "jpg";
        var key = await documents.SaveAsync($"{fileStem}.{extension}", image.Content, cancellationToken).ConfigureAwait(false);
        var stored = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var now = clock.UtcNow;
        UpsertSetting(stored, storageKey, key, now);
        UpsertSetting(stored, contentTypeKey, image.ContentType, now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SystemSettingsDto> DeleteImageAsync(string storageKey, string contentTypeKey, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var setting in stored.Where(s => s.Key == storageKey || s.Key == contentTypeKey))
        {
            settings.Remove(setting);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private void UpsertSetting(List<SystemSetting> stored, string key, string value, DateTimeOffset now)
    {
        var existing = stored.FirstOrDefault(s => s.Key == key);
        if (existing is null)
        {
            var created = SystemSetting.Create(key, value, now);
            settings.Add(created);
            stored.Add(created);
        }
        else if (!string.Equals(existing.Value, value.Trim(), StringComparison.Ordinal))
        {
            existing.Update(value, now);
        }
    }
}
