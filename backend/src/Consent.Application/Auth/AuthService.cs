using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.Maintenance;
using Consent.Domain.Auditing;
using Consent.Domain.Identity;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.Auth;

/// <summary>
/// Đăng nhập, làm mới phiên (xoay vòng refresh token), đăng xuất và đổi mật khẩu.
/// Mọi sự kiện xác thực đều được ghi nhật ký audit.
/// </summary>
public sealed class AuthService(
    IUserRepository users,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IRefreshTokenRepository refreshTokens,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    IRequestContext requestContext,
    IOptions<AuthOptions> options,
    IValidator<LoginRequest> loginValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    MaintenanceService maintenance)
{
    /// <summary>Đang bảo trì (quyết định 53): đúng mật khẩu nhưng tài khoản không có quyền bảo trì.</summary>
    public const string MaintenanceLoginMessage =
        "Hệ thống đang bảo trì. Trong lúc bảo trì chỉ tài khoản có quyền bảo trì hệ thống đăng nhập được.";

    /// <summary>Đang bảo trì: làm mới phiên của tài khoản không có quyền bảo trì.</summary>
    public const string MaintenanceStateMessage = "Hệ thống đang bảo trì. Vui lòng quay lại sau.";

    /// <summary>Giới hạn độ dài refresh token nhận từ client (token thật dài 43 ký tự).</summary>
    private const int MaxRefreshTokenLength = 200;

    private static string? s_dummyPasswordHash;

    private readonly AuthOptions _options = options.Value;

    public async Task<Result<AuthSession>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await loginValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<AuthSession>.Failure(invalid);
        }

        var now = clock.UtcNow;
        var userName = User.NormalizeUserName(request.UserName);
        var user = await users.GetByUserNameAsync(userName, cancellationToken).ConfigureAwait(false);

        // Tài khoản đã xóa (quyết định 51) đăng nhập như tài khoản không tồn tại.
        if (user is null || user.IsDeleted)
        {
            // Vẫn băm mật khẩu để thời gian phản hồi không tiết lộ tài khoản có tồn tại hay không.
            passwordHasher.Verify(GetDummyPasswordHash(), request.Password);
            AddAudit(AuditActions.LoginFailed, user?.Id, userName, new { reason = user is null ? "unknown_user" : "deleted_user" });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(AuthErrors.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            AddAudit(AuditActions.LoginFailed, user.Id, user.UserName, new { reason = "locked_out" });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(AuthErrors.LockedOut(user.LockoutEndUtc!.Value - now));
        }

        var verification = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerification.Failed)
        {
            var lockedNow = user.RegisterFailedLogin(now, _options.ToLockoutPolicy());
            AddAudit(
                lockedNow ? AuditActions.LockedOut : AuditActions.LoginFailed,
                user.Id,
                user.UserName,
                new { reason = "invalid_password" });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(
                lockedNow ? AuthErrors.LockedOut(user.LockoutEndUtc!.Value - now) : AuthErrors.InvalidCredentials);
        }

        // Chỉ báo "tài khoản bị khóa" khi đã đúng mật khẩu, tránh lộ trạng thái tài khoản cho người đoán mật khẩu.
        if (!user.IsActive)
        {
            AddAudit(AuditActions.LoginFailed, user.Id, user.UserName, new { reason = "inactive" });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(AuthErrors.Inactive);
        }

        // Như tài khoản ngưng sử dụng: chỉ báo đang bảo trì khi đã đúng mật khẩu.
        if (await BlockedByMaintenanceAsync(user, MaintenanceLoginMessage, cancellationToken).ConfigureAwait(false) is { } blocked)
        {
            AddAudit(AuditActions.LoginFailed, user.Id, user.UserName, new { reason = "maintenance" });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(blocked);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPassword(passwordHasher.Hash(request.Password), user.MustChangePassword);
        }

        user.RegisterSuccessfulLogin(now);
        var (session, _) = await CreateSessionAsync(user, Guid.CreateVersion7(), now, cancellationToken).ConfigureAwait(false);
        AddAudit(AuditActions.LoginSucceeded, user.Id, user.UserName, null);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<Result<AuthSession>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            return Result<AuthSession>.Failure(AuthErrors.RefreshInvalid);
        }

        var now = clock.UtcNow;
        var stored = await refreshTokens
            .GetByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken)
            .ConfigureAwait(false);
        if (stored is null)
        {
            return Result<AuthSession>.Failure(AuthErrors.RefreshInvalid);
        }

        if (stored.IsRevoked)
        {
            return await HandleRevokedTokenUseAsync(stored, now, cancellationToken).ConfigureAwait(false);
        }

        if (stored.IsExpired(now))
        {
            return Result<AuthSession>.Failure(AuthErrors.RefreshInvalid);
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive || user.IsLockedOut(now))
        {
            await RevokeFamilyAsync(stored.FamilyId, now, RefreshTokenRevocationReasons.UserUnavailable, cancellationToken)
                .ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<AuthSession>.Failure(user is { IsActive: false } ? AuthErrors.Inactive : AuthErrors.RefreshInvalid);
        }

        // Đang bảo trì: không cấp phiên mới nhưng cũng không thu hồi, không xoay vòng refresh token (tắt bảo trì là làm tiếp).
        if (await BlockedByMaintenanceAsync(user, MaintenanceStateMessage, cancellationToken).ConfigureAwait(false) is { } blocked)
        {
            return Result<AuthSession>.Failure(blocked);
        }

        var (session, newToken) = await CreateSessionAsync(user, stored.FamilyId, now, cancellationToken).ConfigureAwait(false);
        stored.Revoke(now, RefreshTokenRevocationReasons.Rotated, newToken.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <summary>Thu hồi refresh token hiện tại. Không báo lỗi nếu token không tồn tại hoặc đã bị thu hồi.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            return;
        }

        var stored = await refreshTokens
            .GetByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken)
            .ConfigureAwait(false);
        if (stored is null || stored.IsRevoked)
        {
            return;
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken).ConfigureAwait(false);
        stored.Revoke(clock.UtcNow, RefreshTokenRevocationReasons.Logout);
        AddAudit(AuditActions.Logout, stored.UserId, user?.UserName, null);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<CurrentUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<CurrentUserDto>.Failure(AuthErrors.SessionInvalid);
        }

        var userRoles = await roles.GetByIdsAsync(user.RoleIds, cancellationToken).ConfigureAwait(false);
        var userDepartments = await GetDepartmentsAsync(user, cancellationToken).ConfigureAwait(false);
        return BuildCurrentUser(user, userRoles, userDepartments);
    }

    /// <summary>
    /// Đổi mật khẩu của chính người dùng. Thu hồi mọi phiên khác và cấp phiên mới cho thiết bị hiện tại.
    /// </summary>
    public async Task<Result<AuthSession>> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await changePasswordValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return Result<AuthSession>.Failure(invalid);
        }

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<AuthSession>.Failure(AuthErrors.SessionInvalid);
        }

        var policyErrors = PasswordPolicy.Validate(request.NewPassword, user.UserName);
        if (policyErrors.Count > 0)
        {
            return Result<AuthSession>.Failure(Error.Validation(
                "validation",
                "Dữ liệu không hợp lệ.",
                new Dictionary<string, string[]> { ["newPassword"] = [.. policyErrors] }));
        }

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerification.Failed)
        {
            return Result<AuthSession>.Failure(AuthErrors.WrongCurrentPassword);
        }

        var now = clock.UtcNow;
        user.SetPassword(passwordHasher.Hash(request.NewPassword), mustChangePassword: false);
        foreach (var token in await refreshTokens.GetActiveByUserAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            token.Revoke(now, RefreshTokenRevocationReasons.PasswordChanged);
        }

        var (session, _) = await CreateSessionAsync(user, Guid.CreateVersion7(), now, cancellationToken).ConfigureAwait(false);
        AddAudit(AuditActions.PasswordChanged, user.Id, user.UserName, null);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<Result<AuthSession>> HandleRevokedTokenUseAsync(
        RefreshToken stored, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!stored.WasRotated)
        {
            return Result<AuthSession>.Failure(AuthErrors.RefreshInvalid);
        }

        var grace = TimeSpan.FromSeconds(Math.Max(0, _options.RefreshReuseGraceSeconds));
        if (now - stored.RevokedAtUtc!.Value <= grace && grace > TimeSpan.Zero)
        {
            return Result<AuthSession>.Failure(AuthErrors.RefreshSuperseded);
        }

        // Token đã bị thay thế lại được dùng: coi như bị lộ, thu hồi toàn bộ họ token.
        await RevokeFamilyAsync(stored.FamilyId, now, RefreshTokenRevocationReasons.ReuseDetected, cancellationToken)
            .ConfigureAwait(false);
        var owner = await users.GetByIdAsync(stored.UserId, cancellationToken).ConfigureAwait(false);
        AddAudit(AuditActions.RefreshTokenReuseDetected, stored.UserId, owner?.UserName, new { familyId = stored.FamilyId });
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<AuthSession>.Failure(AuthErrors.RefreshInvalid);
    }

    /// <summary>
    /// Đang bảo trì mà tài khoản không có quyền bảo trì (người còn phải đổi mật khẩu chưa có quyền nào): lỗi 503 kèm lý do và giờ
    /// dự kiến xong; ngược lại <c>null</c>.
    /// </summary>
    private async Task<Error?> BlockedByMaintenanceAsync(User user, string message, CancellationToken cancellationToken)
    {
        var state = await maintenance.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.Enabled)
        {
            return null;
        }

        var userRoles = await roles.GetByIdsAsync(user.RoleIds, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> permissions = user.MustChangePassword ? [] : PermissionResolver.Resolve(userRoles);
        return MaintenanceService.CanEnterDuringMaintenance(permissions) ? null : MaintenanceService.Blocked(state, message);
    }

    private async Task<(AuthSession Session, RefreshToken Token)> CreateSessionAsync(
        User user, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var userRoles = await roles.GetByIdsAsync(user.RoleIds, cancellationToken).ConfigureAwait(false);
        var userDepartments = await GetDepartmentsAsync(user, cancellationToken).ConfigureAwait(false);
        var currentUser = BuildCurrentUser(user, userRoles, userDepartments);

        var accessToken = tokenService.CreateAccessToken(new AccessTokenClaims(
            user.Id,
            user.UserName,
            user.FullName,
            user.DepartmentId,
            [.. userRoles.Select(r => r.Code)],
            currentUser.Permissions,
            user.MustChangePassword,
            user.AdditionalDepartmentIds));

        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshToken = RefreshToken.Issue(
            user.Id,
            tokenService.HashRefreshToken(rawRefreshToken),
            familyId,
            now,
            TimeSpan.FromHours(_options.RefreshTokenLifetimeHours),
            requestContext.IpAddress);
        refreshTokens.Add(refreshToken);

        var session = new AuthSession(
            accessToken.Value, accessToken.ExpiresAtUtc, rawRefreshToken, refreshToken.ExpiresAtUtc, currentUser);
        return (session, refreshToken);
    }

    /// <summary>Người dùng còn phải đổi mật khẩu thì chưa được cấp quyền nào.</summary>
    private static CurrentUserDto BuildCurrentUser(User user, IReadOnlyList<Role> userRoles, IReadOnlyList<DepartmentRefDto> userDepartments) =>
        new(
            user.Id,
            user.UserName,
            user.FullName,
            user.Title,
            user.DepartmentId,
            userDepartments.FirstOrDefault(d => d.Id == user.DepartmentId)?.Name,
            userDepartments,
            [.. userRoles.OrderBy(r => r.Name, VietnameseComparer.Instance).Select(r => new RoleRefDto(r.Id, r.Code, r.Name))],
            user.MustChangePassword ? [] : PermissionResolver.Resolve(userRoles),
            user.MustChangePassword,
            user.SignatureSpecimenPng is not null);

    /// <summary>Các khoa của tài khoản (quyết định 41): khoa chính đứng đầu, sau đó các khoa khác theo tên.</summary>
    private async Task<IReadOnlyList<DepartmentRefDto>> GetDepartmentsAsync(User user, CancellationToken cancellationToken)
    {
        if (user.DepartmentId is null)
        {
            return [];
        }

        var byId = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);
        return [.. user.DepartmentIds
            .Where(byId.ContainsKey)
            .Select(id => new DepartmentRefDto(id, byId[id].Name))
            .OrderBy(d => d.Id != user.DepartmentId)
            .ThenBy(d => d.Name, VietnameseComparer.Instance)];
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        foreach (var token in await refreshTokens.GetActiveByFamilyAsync(familyId, cancellationToken).ConfigureAwait(false))
        {
            token.Revoke(now, reason);
        }
    }

    private void AddAudit(string action, Guid? userId, string? userName, object? details)
    {
        var actor = new AuditActor(userId, userName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(
            clock.UtcNow,
            action,
            actor,
            entityType: userId is null ? null : nameof(User),
            entityId: userId?.ToString(),
            details: details is null ? null : JsonSerializer.Serialize(details)));
    }

    private string GetDummyPasswordHash()
    {
        var hash = Volatile.Read(ref s_dummyPasswordHash);
        if (hash is not null)
        {
            return hash;
        }

        hash = passwordHasher.Hash(Guid.NewGuid().ToString("N"));
        return Interlocked.CompareExchange(ref s_dummyPasswordHash, hash, null) ?? hash;
    }
}
