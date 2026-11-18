using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Departments;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.Departments;

public sealed class DepartmentService(
    IDepartmentRepository departments,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IValidator<DepartmentRequest> validator,
    IDepartmentSource departmentSource)
{
    private static readonly Error NotFound = Error.NotFound("departments.not_found", "Không tìm thấy khoa/phòng.");

    private static readonly Error ManagedByHis = Error.BusinessRule(
        "departments.managed_by_his",
        "Danh mục khoa/phòng lấy từ HIS: không thêm, sửa, xóa trong phần mềm. Bấm \"Lấy danh mục từ HIS\" để cập nhật.");

    private static readonly Error HisDisabled = Error.BusinessRule(
        "departments.his_disabled",
        "Chưa kết nối API thông tin người bệnh: danh mục khoa/phòng thêm, sửa trong phần mềm.");

    private static readonly Error HisNotConfigured = Error.BusinessRule(
        "departments.his_not_configured",
        "API thông tin người bệnh chưa có câu truy vấn danh mục khoa/phòng (PatientInfo__DepartmentQuery): liên hệ phòng CNTT. Danh mục giữ nguyên.");

    private static readonly Error HisUnavailable = Error.BusinessRule(
        "departments.his_unavailable",
        "Không lấy được danh mục khoa/phòng từ HIS (API thông tin người bệnh hoặc CSDL bệnh viện không trả lời). Danh mục giữ nguyên, thử lại sau.");

    private static readonly Error HisEmpty = Error.BusinessRule(
        "departments.his_empty",
        "HIS không trả về khoa/phòng nào hợp lệ: danh mục giữ nguyên. Kiểm tra câu truy vấn danh mục khoa/phòng.");

    /// <summary>Danh mục khoa/phòng lấy từ HIS (quyết định 42): không thêm, sửa, xóa trong phần mềm.</summary>
    public bool IsManagedByHis => departmentSource.IsEnabled;

    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var items = await departments.ListAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        return [.. items.Select(DepartmentDto.From)];
    }

    public async Task<Result<DepartmentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var department = await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return department is null ? Result<DepartmentDto>.Failure(NotFound) : DepartmentDto.From(department);
    }

    public async Task<Result<DepartmentDto>> CreateAsync(DepartmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsManagedByHis)
        {
            return Result<DepartmentDto>.Failure(ManagedByHis);
        }

        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<DepartmentDto>.Failure(invalid);
        }

        var code = Department.NormalizeCode(request.Code);
        if (await departments.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result<DepartmentDto>.Failure(CodeExists(code));
        }

        var department = Department.Create(code, request.Name);
        if (!request.IsActive)
        {
            department.Update(code, request.Name, isActive: false);
        }

        departments.Add(department);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return DepartmentDto.From(department);
    }

    public async Task<Result<DepartmentDto>> UpdateAsync(Guid id, DepartmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsManagedByHis)
        {
            return Result<DepartmentDto>.Failure(ManagedByHis);
        }

        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<DepartmentDto>.Failure(invalid);
        }

        var department = await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            return Result<DepartmentDto>.Failure(NotFound);
        }

        var code = Department.NormalizeCode(request.Code);
        if (await departments.CodeExistsAsync(code, id, cancellationToken).ConfigureAwait(false))
        {
            return Result<DepartmentDto>.Failure(CodeExists(code));
        }

        department.Update(code, request.Name, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return DepartmentDto.From(department);
    }

    /// <summary>Xóa khoa chưa được sử dụng. Khoa đã có người dùng thì chỉ được ngừng sử dụng.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (IsManagedByHis)
        {
            return Result.Failure(ManagedByHis);
        }

        var department = await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            return Result.Failure(NotFound);
        }

        if (await users.AnyInDepartmentAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(Error.BusinessRule(
                "departments.in_use",
                "Khoa/phòng đang có người dùng nên không thể xóa. Hãy chuyển sang trạng thái ngừng sử dụng."));
        }

        departments.Remove(department);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Lấy danh mục khoa/phòng từ HIS (quyết định 42). Khoa/phòng trùng mã HIS: cập nhật tên, dùng lại nếu đang ngừng sử dụng.
    /// Khoa/phòng nhập tay trước đây (mã không có trên HIS) trùng tên một khoa HIS: nhận mã HIS, giữ người dùng và phiếu đã gắn.
    /// Còn lại thêm mới. Khoa/phòng không còn trên HIS thì ngừng sử dụng (không xóa: phiếu, người dùng còn gắn). HIS trả về
    /// danh mục rỗng hoặc lỗi thì không đổi gì.
    /// </summary>
    public async Task<Result<DepartmentSyncResultDto>> SyncFromHisAsync(CancellationToken cancellationToken)
    {
        if (!IsManagedByHis)
        {
            return Result<DepartmentSyncResultDto>.Failure(HisDisabled);
        }

        var source = await departmentSource.GetAsync(cancellationToken).ConfigureAwait(false);
        switch (source.Status)
        {
            case DepartmentSourceStatus.Found:
                break;
            case DepartmentSourceStatus.NotConfigured:
                return Result<DepartmentSyncResultDto>.Failure(HisNotConfigured);
            case DepartmentSourceStatus.Disabled:
                return Result<DepartmentSyncResultDto>.Failure(HisDisabled);
            default:
                return Result<DepartmentSyncResultDto>.Failure(HisUnavailable);
        }

        var (incoming, skipped) = Normalize(source.Items);
        if (incoming.Count == 0)
        {
            return Result<DepartmentSyncResultDto>.Failure(HisEmpty);
        }

        var existing = await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        var byCode = existing.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        var hisCodes = incoming.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Khoa/phòng có mã không có trên HIS (nhập tay trước đây): ghép theo tên, khoa đang sử dụng trước.
        var unmatched = existing.Where(d => !hisCodes.Contains(d.Code)).OrderByDescending(d => d.IsActive).ToList();
        int added = 0, updated = 0, unchanged = 0;
        foreach (var (code, name) in incoming)
        {
            var department = byCode.GetValueOrDefault(code)
                ?? unmatched.FirstOrDefault(d => NameKey(d.Name) == NameKey(name));
            if (department is null)
            {
                departments.Add(Department.Create(code, name));
                added++;
                continue;
            }

            unmatched.Remove(department);
            if (department.SyncFromHis(code, name))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        List<string> deactivated = [];
        foreach (var department in unmatched.Where(d => d.IsActive))
        {
            department.Deactivate();
            deactivated.Add(department.Name);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DepartmentSyncResultDto(incoming.Count, added, updated, unchanged, deactivated, skipped);
    }

    /// <summary>
    /// Chuẩn hóa danh mục HIS: mã viết hoa (mã rỗng hoặc dài quá thì bỏ qua), tên cùng dạng Unicode với chữ gõ trên trình duyệt
    /// (thiếu tên thì lấy mã, dài quá thì cắt); mã lặp lại thì lấy dòng đầu.
    /// </summary>
    private static (List<(string Code, string Name)> Incoming, List<string> Skipped) Normalize(IEnumerable<ExternalDepartment> items)
    {
        List<(string Code, string Name)> incoming = [];
        List<string> skipped = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var code = (item.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length is 0 or > Department.CodeMaxLength)
            {
                skipped.Add(code);
                continue;
            }

            if (!seen.Add(code))
            {
                continue;
            }

            var name = (item.Name ?? string.Empty).Normalize(NormalizationForm.FormC).Trim();
            name = name.Length == 0 ? code : name[..Math.Min(name.Length, Department.NameMaxLength)].TrimEnd();
            incoming.Add((code, name));
        }

        return (incoming, skipped);
    }

    /// <summary>Khóa so tên khoa/phòng: cùng dạng Unicode, bỏ khoảng trắng thừa, không phân biệt hoa thường.</summary>
    private static string NameKey(string name) =>
        string.Join(' ', name.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static Error CodeExists(string code) =>
        Error.Conflict("departments.code_exists", $"Mã khoa \"{code}\" đã tồn tại.");
}
