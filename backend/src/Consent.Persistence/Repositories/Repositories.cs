using Consent.Application.Abstractions;
using Consent.Application.Auditing;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Shared;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

internal sealed class UserRepository(ConsentDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserName == normalizedUserName, cancellationToken);

    public Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.UserName == normalizedUserName, cancellationToken);

    public Task<bool> AnyInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.DepartmentId == departmentId, cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => db.Users.AnyAsync(cancellationToken);

    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludeUserId, CancellationToken cancellationToken) =>
        db.Users.CountAsync(
            u => u.IsActive && u.Id != excludeUserId && u.Roles.Any(r => r.RoleId == roleId),
            cancellationToken);

    public async Task<PagedResult<UserListItemDto>> SearchAsync(
        UserQuery query, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            users = users.Where(u => u.FullName.Contains(keyword) || u.UserName.Contains(keyword)
                                     || (u.Title != null && u.Title.Contains(keyword)));
        }

        if (query.DepartmentId is { } departmentId)
        {
            users = users.Where(u => u.DepartmentId == departmentId);
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        var total = await users.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await users
            .OrderBy(u => u.FullName).ThenBy(u => u.UserName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FullName,
                u.Title,
                u.DepartmentId,
                DepartmentName = db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                RoleNames = db.Roles
                    .Where(r => u.Roles.Any(ur => ur.RoleId == r.Id))
                    .OrderBy(r => r.Name)
                    .Select(r => r.Name)
                    .ToList(),
                u.IsActive,
                u.LockoutEndUtc,
                u.LastLoginAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.ConvertAll(r => new UserListItemDto(
            r.Id,
            r.UserName,
            r.FullName,
            r.Title,
            r.DepartmentId,
            r.DepartmentName,
            r.RoleNames,
            r.IsActive,
            r.LockoutEndUtc > nowUtc,
            r.LastLoginAtUtc));
        return new PagedResult<UserListItemDto>(items, query.Page, query.PageSize, total);
    }

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class RoleRepository(ConsentDbContext db) : IRoleRepository
{
    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        await db.Roles.Include(r => r.Permissions).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Roles.Include(r => r.Permissions)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class DepartmentRepository(ConsentDbContext db) : IDepartmentRepository
{
    public async Task<IReadOnlyList<Department>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.Departments
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Departments.AnyAsync(d => d.Code == normalizedCode && d.Id != excludeId, cancellationToken);

    public void Add(Department department) => db.Departments.Add(department);

    public void Remove(Department department) => db.Departments.Remove(department);
}

internal sealed class RefreshTokenRepository(ConsentDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}

internal sealed class AuditLogRepository(ConsentDbContext db) : IAuditLogRepository
{
    public void Add(AuditLog auditLog) => db.AuditLogs.Add(auditLog);

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var logs = db.AuditLogs.AsNoTracking();

        if (query.UserId is { } userId)
        {
            logs = logs.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            logs = logs.Where(a => a.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            logs = logs.Where(a => a.EntityId == query.EntityId);
        }

        if (query.FromUtc is { } from)
        {
            logs = logs.Where(a => a.OccurredAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            logs = logs.Where(a => a.OccurredAtUtc <= to);
        }

        var total = await logs.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await logs
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new AuditLogDto(
                a.Id, a.OccurredAtUtc, a.Action, a.EntityType, a.EntityId, a.UserId, a.UserName, a.IpAddress, a.CorrelationId, a.Details))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }
}
