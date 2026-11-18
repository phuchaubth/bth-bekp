using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Roles;

public sealed class RoleService(
    IRoleRepository roles,
    IUserRepository users,
    IConsentTemplateRepository templates,
    IUnitOfWork unitOfWork)
{
    public static IReadOnlyList<PermissionDto> GetPermissionCatalog() => [.. PermissionCatalog.All.Select(PermissionDto.From)];

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.OrderByDescending(r => r.IsAdministrator).ThenBy(r => r.Name, VietnameseComparer.Instance).Select(RoleDto.From)];
    }

    /// <summary>Thêm vai trò (quyết định 40): tên, mô tả, quyền; mã vai trò sinh từ tên và không đổi về sau.</summary>
    public async Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var all = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        var permissions = request.Permissions ?? [];
        if ((ValidateDetails(request.Name, request.Description, all, exceptId: null) ?? ValidatePermissions(permissions)) is { } error)
        {
            return Result<RoleDto>.Failure(error);
        }

        var role = Role.Create(RoleCodes.FromName(request.Name, all.Select(r => r.Code)), request.Name, request.Description, isSystem: false);
        role.SetPermissions(permissions);
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleDto.From(role);
    }

    /// <summary>Đổi tên, mô tả (cả vai trò có sẵn). Mã vai trò giữ nguyên nên quyền và cấu hình ký của mẫu phiếu không đổi.</summary>
    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var all = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        var role = all.FirstOrDefault(r => r.Id == id);
        if (role is null)
        {
            return Result<RoleDto>.Failure(NotFound());
        }

        if (ValidateDetails(request.Name, request.Description, all, exceptId: id) is { } error)
        {
            return Result<RoleDto>.Failure(error);
        }

        role.UpdateDetails(request.Name, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleDto.From(role);
    }

    /// <summary>
    /// Xóa vai trò do quản trị tạo. Không xóa vai trò có sẵn, vai trò còn gán cho tài khoản (kể cả tài khoản đã khóa), hay vai
    /// trò đang dùng trong cấu hình ký của mẫu phiếu (mọi phiên bản: phiếu đang chờ xác nhận dùng phiên bản đã phát hành).
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await roles.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Result.Failure(NotFound());
        }

        if (role.IsSystem)
        {
            return Result.Failure(Error.BusinessRule(
                "roles.system_role", "Vai trò có sẵn của hệ thống không xóa được, chỉ đổi được tên, mô tả và quyền."));
        }

        var userCount = await users.CountUsersInRoleAsync(role.Id, cancellationToken).ConfigureAwait(false);
        if (userCount > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "roles.in_use",
                $"Vai trò đang gán cho {userCount} tài khoản. Bỏ vai trò này khỏi các tài khoản đó (Quản trị → Người dùng) rồi xóa."));
        }

        var usedBy = await TemplatesUsingAsync(role.Code, cancellationToken).ConfigureAwait(false);
        if (usedBy.Count > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "roles.used_by_templates",
                $"Vai trò đang dùng trong cấu hình ký của mẫu phiếu: {string.Join(", ", usedBy)}. Chọn vai trò khác cho các mẫu đó rồi xóa."));
        }

        roles.Remove(role);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<RoleDto>> UpdatePermissionsAsync(
        Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var role = await roles.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Result<RoleDto>.Failure(NotFound());
        }

        if (role.IsAdministrator)
        {
            return Result<RoleDto>.Failure(Error.BusinessRule(
                "roles.admin_immutable", "Vai trò Quản trị hệ thống luôn có toàn bộ quyền và không thể thay đổi."));
        }

        if (role.IsKiosk)
        {
            return Result<RoleDto>.Failure(Error.BusinessRule(
                "roles.kiosk_immutable",
                "Vai trò Kiosk chỉ có quyền dùng màn hình kiosk (máy đặt ở nơi công cộng không được xem dữ liệu khác) và không thể thay đổi."));
        }

        var requested = request.Permissions ?? [];
        if (ValidatePermissions(requested) is { } error)
        {
            return Result<RoleDto>.Failure(error);
        }

        role.SetPermissions(requested);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleDto.From(role);
    }

    private static Error NotFound() => Error.NotFound("roles.not_found", "Không tìm thấy vai trò.");

    private static Error? ValidateDetails(string? name, string? description, IEnumerable<Role> all, Guid? exceptId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Error.ValidationField("name", "Nhập tên vai trò.");
        }

        if (trimmed.Length > Role.NameMaxLength)
        {
            return Error.ValidationField("name", $"Tên vai trò tối đa {Role.NameMaxLength} ký tự.");
        }

        if (description?.Trim().Length > Role.DescriptionMaxLength)
        {
            return Error.ValidationField("description", $"Mô tả tối đa {Role.DescriptionMaxLength} ký tự.");
        }

        var key = NameKey(trimmed);
        return all.Any(r => r.Id != exceptId && NameKey(r.Name) == key)
            ? Error.ValidationField("name", $"Đã có vai trò tên \"{trimmed}\".")
            : null;
    }

    /// <summary>Tên so sánh không phân biệt hoa thường, khoảng trắng thừa và cách gõ dấu (dựng sẵn hay tổ hợp).</summary>
    private static string NameKey(string name) =>
        string.Join(' ', name.Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static Error? ValidatePermissions(IEnumerable<string> permissions)
    {
        var codes = permissions.ToArray();
        var unknown = codes.Where(code => !PermissionCatalog.IsDefined(code)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            return Error.ValidationField("permissions", $"Quyền không tồn tại: {string.Join(", ", unknown)}.");
        }

        // Quyết định 24: quyền dùng kiosk chỉ thuộc vai trò Kiosk (tài khoản kiosk không có vai trò khác).
        return codes.Contains(Permissions.KioskUse, StringComparer.Ordinal)
            ? Error.ValidationField("permissions", "Quyền dùng kiosk chỉ dành cho vai trò Kiosk người bệnh.")
            : null;
    }

    /// <summary>Tên các mẫu phiếu dùng vai trò ở bước xác nhận cuối hoặc ở bước xác nhận trung gian của phiên bản nào đó.</summary>
    private async Task<IReadOnlyList<string>> TemplatesUsingAsync(string code, CancellationToken cancellationToken)
    {
        var all = await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        return [.. all
            .Where(t => string.Equals(t.HospitalSignerRoleCode, code, StringComparison.OrdinalIgnoreCase)
                || t.Versions.Any(v => StepRoleCodes(v.ContentJson).Contains(code, StringComparer.OrdinalIgnoreCase)))
            .Select(t => t.Name)
            .Order(VietnameseComparer.Instance)];
    }

    private static IEnumerable<string> StepRoleCodes(string contentJson) =>
        TemplateJson.TryDeserialize(contentJson, out _)?.Signatures?.Steps?.Select(step => step.RoleCode) ?? [];
}
