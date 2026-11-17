using System.Security.Claims;
using Consent.Application.Abstractions;
using Consent.Infrastructure.Security;

namespace Consent.Api.Infrastructure;

/// <summary>Người dùng hiện tại, đọc từ claim của access token. Ngoài HTTP request (ví dụ seeding) là ẩn danh.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated => Principal is not null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(AppClaimTypes.Subject), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(AppClaimTypes.UserName);

    public Guid? DepartmentId => Guid.TryParse(Principal?.FindFirstValue(AppClaimTypes.Department), out var id) ? id : null;

    /// <summary>Khoa chính rồi các khoa khác. Token cấp trước khi có khoa khác (quyết định 41) chỉ có khoa chính.</summary>
    public IReadOnlyList<Guid> DepartmentIds =>
        DepartmentId is { } primary
            ? [primary, .. ParseIds(AppClaimTypes.AdditionalDepartment).Where(id => id != primary)]
            : [];

    public bool HasPermission(string permission) => Principal?.HasClaim(AppClaimTypes.Permission, permission) == true;

    public bool IsInRole(string roleCode) => Principal?.HasClaim(AppClaimTypes.Role, roleCode) == true;

    public IReadOnlyList<Guid> SigningFormIds => ParseIds(AppClaimTypes.SigningForm);

    public Guid? SigningSessionId => Guid.TryParse(Principal?.FindFirstValue(AppClaimTypes.SigningSession), out var id) ? id : null;

    private List<Guid> ParseIds(string claimType) =>
        Principal?.FindAll(claimType)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];
}

internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
