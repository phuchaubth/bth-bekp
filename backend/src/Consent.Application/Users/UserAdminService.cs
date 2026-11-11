using Consent.Application.Abstractions;
using Consent.Application.Auth;
using Consent.Application.Common;
using Consent.Domain.Identity;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.Users;

/// <summary>Quản trị tài khoản người dùng: tạo, sửa, khóa/mở khóa, đặt lại mật khẩu.</summary>
public sealed class UserAdminService(
    IUserRepository users,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IClock clock,
    ICurrentUser currentUser,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator)
{
    private static readonly Error NotFound = Error.NotFound("users.not_found", "Không tìm thấy người dùng.");

    private static readonly Error LastAdministrator = Error.BusinessRule(
        "users.last_administrator", "Hệ thống phải luôn còn ít nhất một quản trị viên đang hoạt động.");

    public Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return users.SearchAsync(query with { Page = page, PageSize = pageSize }, clock.UtcNow, cancellationToken);
    }

    public async Task<Result<UserDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return user is null
            ? Result<UserDetailDto>.Failure(NotFound)
            : await ToDetailAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserDetailDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await createValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<UserDetailDto>.Failure(invalid);
        }

        var userName = User.NormalizeUserName(request.UserName);
        if (await users.UserNameExistsAsync(userName, cancellationToken).ConfigureAwait(false))
        {
            return Result<UserDetailDto>.Failure(
                Error.Conflict("users.username_exists", $"Tên đăng nhập \"{userName}\" đã tồn tại."));
        }

        if (await ValidateReferencesAsync(request, cancellationToken).ConfigureAwait(false) is { } referenceError)
        {
            return Result<UserDetailDto>.Failure(referenceError);
        }

        var user = User.Create(userName, request.FullName, clock.UtcNow);
        user.UpdateProfile(
            request.FullName, request.Email, request.PhoneNumber, request.Title, request.DepartmentId, request.SmartCaUserId);
        user.SetRoles(request.RoleIds);
        user.SetPassword(passwordHasher.Hash(request.Password), mustChangePassword: true);
        users.Add(user);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDetailAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserDetailDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await updateValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<UserDetailDto>.Failure(invalid);
        }

        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result<UserDetailDto>.Failure(NotFound);
        }

        if (await ValidateReferencesAsync(request, cancellationToken).ConfigureAwait(false) is { } referenceError)
        {
            return Result<UserDetailDto>.Failure(referenceError);
        }

        var removesAdministrator = await IsAdministratorAsync(user.RoleIds, cancellationToken).ConfigureAwait(false)
            && !await IsAdministratorAsync(request.RoleIds, cancellationToken).ConfigureAwait(false);
        if (removesAdministrator && user.IsActive
            && !await HasOtherActiveAdministratorAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result<UserDetailDto>.Failure(LastAdministrator);
        }

        user.UpdateProfile(
            request.FullName, request.Email, request.PhoneNumber, request.Title, request.DepartmentId, request.SmartCaUserId);
        user.SetRoles(request.RoleIds);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDetailAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Khóa tài khoản: không đăng nhập được và mọi phiên đang mở bị thu hồi.</summary>
    public async Task<Result> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == id)
        {
            return Result.Failure(Error.BusinessRule("users.cannot_lock_self", "Bạn không thể tự khóa tài khoản của mình."));
        }

        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(NotFound);
        }

        if (!user.IsActive)
        {
            return Result.Success();
        }

        if (await IsAdministratorAsync(user.RoleIds, cancellationToken).ConfigureAwait(false)
            && !await HasOtherActiveAdministratorAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(LastAdministrator);
        }

        user.Deactivate();
        await RevokeSessionsAsync(user.Id, RefreshTokenRevocationReasons.UserDeactivated, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Mở khóa: cho phép đăng nhập lại, xóa trạng thái khóa tạm thời do đăng nhập sai.</summary>
    public async Task<Result> UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(NotFound);
        }

        user.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Đặt lại mật khẩu; người dùng phải đổi mật khẩu ở lần đăng nhập tiếp theo.</summary>
    public async Task<Result> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await resetPasswordValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(NotFound);
        }

        var policyErrors = PasswordPolicy.Validate(request.NewPassword, user.UserName);
        if (policyErrors.Count > 0)
        {
            return Result.Failure(Error.Validation(
                "validation", "Dữ liệu không hợp lệ.", new Dictionary<string, string[]> { ["newPassword"] = [.. policyErrors] }));
        }

        user.SetPassword(passwordHasher.Hash(request.NewPassword), mustChangePassword: true);
        await RevokeSessionsAsync(user.Id, RefreshTokenRevocationReasons.PasswordReset, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Error?> ValidateReferencesAsync(IUserProfileRequest request, CancellationToken cancellationToken)
    {
        if (request.DepartmentId is { } departmentId)
        {
            var department = await departments.GetByIdAsync(departmentId, cancellationToken).ConfigureAwait(false);
            if (department is null || !department.IsActive)
            {
                return Error.ValidationField("departmentId", "Khoa/phòng không tồn tại hoặc đã ngừng sử dụng.");
            }
        }

        var requestedRoleIds = request.RoleIds.Distinct().ToArray();
        var found = await roles.GetByIdsAsync(requestedRoleIds, cancellationToken).ConfigureAwait(false);
        return found.Count == requestedRoleIds.Length
            ? null
            : Error.ValidationField("roleIds", "Có vai trò không tồn tại.");
    }

    private async Task<bool> IsAdministratorAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var assigned = await roles.GetByIdsAsync(roleIds, cancellationToken).ConfigureAwait(false);
        return assigned.Any(r => r.IsAdministrator);
    }

    private async Task<bool> HasOtherActiveAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var administratorRole = (await roles.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.IsAdministrator);
        return administratorRole is not null
            && await users.CountActiveUsersInRoleAsync(administratorRole.Id, userId, cancellationToken).ConfigureAwait(false) > 0;
    }

    private async Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        foreach (var token in await refreshTokens.GetActiveByUserAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            token.Revoke(now, reason);
        }
    }

    private async Task<UserDetailDto> ToDetailAsync(User user, CancellationToken cancellationToken)
    {
        var userRoles = await roles.GetByIdsAsync(user.RoleIds, cancellationToken).ConfigureAwait(false);
        string? departmentName = null;
        if (user.DepartmentId is { } departmentId)
        {
            departmentName = (await departments.GetByIdAsync(departmentId, cancellationToken).ConfigureAwait(false))?.Name;
        }

        var now = clock.UtcNow;
        return new UserDetailDto(
            user.Id,
            user.UserName,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.Title,
            user.DepartmentId,
            departmentName,
            user.SmartCaUserId,
            [.. userRoles.OrderBy(r => r.Name, VietnameseComparer.Instance).Select(r => new RoleRefDto(r.Id, r.Code, r.Name))],
            user.IsActive,
            user.IsLockedOut(now),
            user.LockoutEndUtc,
            user.MustChangePassword,
            user.LastLoginAtUtc,
            user.CreatedAtUtc);
    }
}
