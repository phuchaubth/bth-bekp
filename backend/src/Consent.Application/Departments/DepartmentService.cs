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
    IValidator<DepartmentRequest> validator)
{
    private static readonly Error NotFound = Error.NotFound("departments.not_found", "Không tìm thấy khoa/phòng.");

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

    private static Error CodeExists(string code) =>
        Error.Conflict("departments.code_exists", $"Mã khoa \"{code}\" đã tồn tại.");
}
