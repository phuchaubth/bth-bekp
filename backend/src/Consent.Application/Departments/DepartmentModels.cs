using Consent.Domain.Departments;
using FluentValidation;

namespace Consent.Application.Departments;

public sealed record DepartmentDto(Guid Id, string Code, string Name, bool IsActive)
{
    public static DepartmentDto From(Department department)
    {
        ArgumentNullException.ThrowIfNull(department);
        return new DepartmentDto(department.Id, department.Code, department.Name, department.IsActive);
    }
}

/// <summary>Dữ liệu tạo/sửa khoa/phòng.</summary>
public sealed record DepartmentRequest(string Code, string Name, bool IsActive = true);

public sealed class DepartmentRequestValidator : AbstractValidator<DepartmentRequest>
{
    public DepartmentRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã khoa.")
            .MaximumLength(Department.CodeMaxLength).WithMessage($"Mã khoa tối đa {Department.CodeMaxLength} ký tự.")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Mã khoa chỉ gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên khoa.")
            .MaximumLength(Department.NameMaxLength).WithMessage($"Tên khoa tối đa {Department.NameMaxLength} ký tự.");
    }
}
