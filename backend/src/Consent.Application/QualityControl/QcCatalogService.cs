using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.QualityControl;
using Consent.Shared;

namespace Consent.Application.QualityControl;

/// <summary>
/// Danh mục nội kiểm đường huyết tại giường (quyết định 57), Khoa HSH khai báo (<see cref="Permissions.QcManage"/>): máy đo của
/// từng khoa, lô que thử, lô dung dịch chứng kèm mức và ngưỡng chấp nhận. Không xóa: hết dùng thì ngưng sử dụng. Mọi thay đổi
/// được ghi nhật ký hệ thống.
/// </summary>
public sealed class QcCatalogService(
    IQualityControlRepository qc,
    IDepartmentRepository departments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly Error Forbidden = Error.Forbidden("qc.forbidden", "Chỉ Khoa HSH khai báo danh mục nội kiểm.");

    private bool CanManage => currentUser.HasPermission(Permissions.QcManage);

    /// <summary>Máy đo theo khoa, rồi theo loại máy và số seri; lô theo loại, mức, hạn dùng xa trước.</summary>
    public async Task<Result<QcCatalogDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbidden;
        }

        var names = await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false);
        var meters = (await qc.ListMetersAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => ToDto(m, names))
            .OrderBy(m => m.DepartmentName, VietnameseComparer.Instance)
            .ThenBy(m => m.Model, VietnameseComparer.Instance)
            .ThenBy(m => m.SerialNumber, StringComparer.Ordinal)
            .ToList();
        var lots = (await qc.ListLotsAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Level)
            .ThenByDescending(l => l.IsActive)
            .ThenByDescending(l => l.ExpiryDate)
            .ThenBy(l => l.LotNumber, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
        return new QcCatalogDto(meters, lots);
    }

    public async Task<Result<QcMeterDto>> CreateMeterAsync(SaveQcMeterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage)
        {
            return Forbidden;
        }

        if (await ValidateMeterAsync(request, null, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var meter = QcMeter.Create(request.DepartmentId!.Value, request.Model!, request.SerialNumber!, clock.UtcNow);
        qc.Add(meter);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(meter, await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sửa máy đo; chuyển máy sang khoa khác thì khoa mới mở sổ mới, sổ của khoa cũ giữ nguyên.</summary>
    public async Task<Result<QcMeterDto>> UpdateMeterAsync(Guid id, SaveQcMeterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage)
        {
            return Forbidden;
        }

        if (await qc.GetMeterAsync(id, cancellationToken).ConfigureAwait(false) is not { } meter)
        {
            return Error.NotFound("qc.meter_not_found", "Không tìm thấy máy đo.");
        }

        if (await ValidateMeterAsync(request, id, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        meter.Update(request.DepartmentId!.Value, request.Model!, request.SerialNumber!, request.IsActive ?? meter.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(meter, await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<QcLotDto>> CreateLotAsync(SaveQcLotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage)
        {
            return Forbidden;
        }

        if (request.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return Error.ValidationField("kind", "Chọn loại vật tư.");
        }

        if (await ValidateLotAsync(kind, request, null, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        try
        {
            var now = clock.UtcNow;
            var lot = kind == QcLotKind.TestStrip
                ? QcLot.CreateTestStrip(request.ProductName!, request.LotNumber!, request.ExpiryDate!.Value, now)
                : QcLot.CreateControl(
                    request.ProductName!,
                    request.LotNumber!,
                    request.ExpiryDate!.Value,
                    request.Level!.Value,
                    request.RangeLow!.Value,
                    request.RangeHigh!.Value,
                    request.Unit!.Value,
                    now);
            qc.Add(lot);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDto(lot);
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("qc.rule", ex.Message);
        }
    }

    /// <summary>Sửa lô (không đổi loại vật tư): chỉ áp dụng cho kết quả ghi sau khi sửa, kết quả đã ghi giữ nguyên ngưỡng lúc ghi.</summary>
    public async Task<Result<QcLotDto>> UpdateLotAsync(Guid id, SaveQcLotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage)
        {
            return Forbidden;
        }

        if (await qc.GetLotAsync(id, cancellationToken).ConfigureAwait(false) is not { } lot)
        {
            return Error.NotFound("qc.lot_not_found", "Không tìm thấy lô.");
        }

        if (request.Kind is { } kind && kind != lot.Kind)
        {
            return Error.ValidationField("kind", "Không đổi được loại vật tư của lô đã khai báo.");
        }

        if (await ValidateLotAsync(lot.Kind, request, id, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        try
        {
            var control = lot.Kind == QcLotKind.ControlSolution;
            lot.Update(
                request.ProductName!,
                request.LotNumber!,
                request.ExpiryDate!.Value,
                control ? request.Level : null,
                control ? request.RangeLow : null,
                control ? request.RangeHigh : null,
                control ? request.Unit : null,
                request.IsActive ?? lot.IsActive);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDto(lot);
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("qc.rule", ex.Message);
        }
    }

    internal static QcLotDto ToDto(QcLot lot) => new(
        lot.Id, lot.Kind, lot.ProductName, lot.LotNumber, lot.ExpiryDate, lot.Level, lot.RangeLow, lot.RangeHigh, lot.Unit, lot.IsActive,
        lot.CreatedAtUtc);

    private static QcMeterDto ToDto(QcMeter meter, IReadOnlyDictionary<Guid, string> names) => new(
        meter.Id, meter.DepartmentId, names.GetValueOrDefault(meter.DepartmentId, "Không rõ"), meter.Model, meter.SerialNumber,
        meter.IsActive, meter.CreatedAtUtc);

    private static void Length(Dictionary<string, string[]> errors, string field, string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"Nhập {name}."];
        }
        else if (value.Trim().Length > max)
        {
            errors[field] = [$"Tối đa {max} ký tự."];
        }
    }

    private async Task<Error?> ValidateMeterAsync(SaveQcMeterRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.DepartmentId is not { } departmentId
            || await departments.GetByIdAsync(departmentId, cancellationToken).ConfigureAwait(false) is not { IsActive: true })
        {
            errors["departmentId"] = ["Chọn khoa đang hoạt động."];
        }

        Length(errors, "model", request.Model, QcMeter.ModelMaxLength, "loại máy");
        Length(errors, "serialNumber", request.SerialNumber, QcMeter.SerialMaxLength, "số seri máy");
        if (!errors.ContainsKey("serialNumber")
            && await qc.SerialExistsAsync(request.SerialNumber!.Trim(), id, cancellationToken).ConfigureAwait(false))
        {
            errors["serialNumber"] = ["Số seri này đã khai báo cho máy khác."];
        }

        return errors.Count > 0 ? Error.Validation("validation", "Dữ liệu không hợp lệ.", errors) : null;
    }

    private async Task<Error?> ValidateLotAsync(QcLotKind kind, SaveQcLotRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Length(errors, "productName", request.ProductName, QcLot.ProductMaxLength, "tên vật tư");
        Length(errors, "lotNumber", request.LotNumber, QcLot.LotNumberMaxLength, "số lô");
        if (request.ExpiryDate is null)
        {
            errors["expiryDate"] = ["Nhập hạn dùng."];
        }

        var control = kind == QcLotKind.ControlSolution;
        if (control)
        {
            if (request.Level is not (>= QcLot.MinLevel and <= QcLot.MaxLevel))
            {
                errors["level"] = [$"Chọn mức từ {QcLot.MinLevel} đến {QcLot.MaxLevel}."];
            }

            var valueError = $"Nhập số lớn hơn 0, tối đa {QcLot.Format(QcLot.MaxValue)}, một chữ số thập phân.";
            if (request.RangeLow is not { } low || !QcLot.IsValidValue(low))
            {
                errors["rangeLow"] = [valueError];
            }

            if (request.RangeHigh is not { } high || !QcLot.IsValidValue(high))
            {
                errors["rangeHigh"] = [valueError];
            }
            else if (request.RangeLow is { } lowValue && QcLot.IsValidValue(lowValue) && lowValue >= high)
            {
                errors["rangeHigh"] = ["Ngưỡng trên phải lớn hơn ngưỡng dưới."];
            }

            if (request.Unit is not { } unit || !Enum.IsDefined(unit))
            {
                errors["unit"] = ["Chọn đơn vị."];
            }
        }

        if (!errors.ContainsKey("lotNumber") && !errors.ContainsKey("level")
            && await qc.LotExistsAsync(kind, request.LotNumber!.Trim(), control ? request.Level : null, id, cancellationToken)
                .ConfigureAwait(false))
        {
            errors["lotNumber"] = [control ? "Lô dung dịch chứng mức này đã khai báo." : "Lô que thử này đã khai báo."];
        }

        return errors.Count > 0 ? Error.Validation("validation", "Dữ liệu không hợp lệ.", errors) : null;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> DepartmentNamesAsync(CancellationToken cancellationToken) =>
        (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Name);
}
