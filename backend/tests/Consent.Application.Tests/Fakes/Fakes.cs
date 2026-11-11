using System.Security.Cryptography;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Auditing;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Băm giả lập: "hashed:" + mật khẩu; "legacy:" + mật khẩu mô phỏng cần băm lại.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed:" + password;

    public PasswordVerification Verify(string passwordHash, string password) =>
        passwordHash == "hashed:" + password ? PasswordVerification.Success
        : passwordHash == "legacy:" + password ? PasswordVerification.SuccessRehashNeeded
        : PasswordVerification.Failed;
}

internal sealed class FakeTokenService(IClock clock) : ITokenService
{
    private int _counter;

    public AccessTokenClaims? LastClaims { get; private set; }

    public AccessToken CreateAccessToken(AccessTokenClaims claims)
    {
        LastClaims = claims;
        return new AccessToken($"access-{++_counter}", clock.UtcNow.AddMinutes(15));
    }

    public string GenerateRefreshToken() => "refresh-" + Guid.NewGuid().ToString("N");

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}

internal sealed class FakeRequestContext : IRequestContext
{
    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "unit-test";

    public string? CorrelationId => "trace-unit-test";
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated => UserId is not null;

    public string? UserName { get; set; }

    public Guid? DepartmentId { get; set; }

    public HashSet<string> Permissions { get; } = [];

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

/// <summary>"Cơ sở dữ liệu" trong bộ nhớ dùng chung cho các repository giả.</summary>
internal sealed class InMemoryStore : IUnitOfWork
{
    public List<User> Users { get; } = [];

    public List<Role> Roles { get; } = [];

    public List<Department> Departments { get; } = [];

    public List<RefreshToken> RefreshTokens { get; } = [];

    public List<AuditLog> AuditLogs { get; } = [];

    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUserRepository(InMemoryStore store) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Find(u => u.Id == id));

    public Task<User?> GetByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Find(u => u.UserName == normalizedUserName));

    public Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Exists(u => u.UserName == normalizedUserName));

    public Task<bool> AnyInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Exists(u => u.DepartmentId == departmentId));

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(store.Users.Count > 0);

    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludeUserId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Count(u => u.IsActive && u.Id != excludeUserId && u.RoleIds.Contains(roleId)));

    public Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery query, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var items = store.Users
            .Where(u => query.Keyword is null || u.FullName.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase))
            .Select(u => new UserListItemDto(u.Id, u.UserName, u.FullName, u.Title, u.DepartmentId, null, [], u.IsActive, u.IsLockedOut(nowUtc), u.LastLoginAtUtc))
            .ToList();
        return Task.FromResult(new PagedResult<UserListItemDto>(items, query.Page, query.PageSize, items.Count));
    }

    public void Add(User user) => store.Users.Add(user);
}

internal sealed class InMemoryRoleRepository(InMemoryStore store) : IRoleRepository
{
    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. store.Roles]);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Roles.Find(r => r.Id == id));

    public Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. store.Roles.Where(r => ids.Contains(r.Id))]);
}

internal sealed class InMemoryDepartmentRepository(InMemoryStore store) : IDepartmentRepository
{
    public Task<IReadOnlyList<Department>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Department>>([.. store.Departments.Where(d => includeInactive || d.IsActive)]);

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Departments.Find(d => d.Id == id));

    public Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Departments.Exists(d => d.Code == normalizedCode && d.Id != excludeId));

    public void Add(Department department) => store.Departments.Add(department);

    public void Remove(Department department) => store.Departments.Remove(department);
}

internal sealed class InMemoryRefreshTokenRepository(InMemoryStore store) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(store.RefreshTokens.Find(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>([.. store.RefreshTokens.Where(t => t.FamilyId == familyId && !t.IsRevoked)]);

    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>([.. store.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked)]);

    public void Add(RefreshToken token) => store.RefreshTokens.Add(token);
}

internal sealed class InMemoryAuditLogRepository(InMemoryStore store) : IAuditLogRepository
{
    public void Add(AuditLog auditLog) => store.AuditLogs.Add(auditLog);

    public Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<AuditLogDto>([], query.Page, query.PageSize, 0));
}
