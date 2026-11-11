using System.Collections.Concurrent;
using Consent.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Consent.Api.Authorization;

/// <summary>Yêu cầu người dùng có quyền (permission) cụ thể. Ví dụ: <c>[HasPermission(Permissions.UsersManage)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + permission)
{
    public string Permission { get; } = permission;
}

/// <summary>Tạo policy động "perm:&lt;mã quyền&gt;" = đã đăng nhập + có claim quyền tương ứng trong access token.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _defaultProvider = new(options);
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _cache = new(StringComparer.Ordinal);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _defaultProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _defaultProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return _defaultProvider.GetPolicyAsync(policyName);
        }

        var policy = _cache.GetOrAdd(policyName, static name => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AppClaimTypes.Permission, name[PolicyPrefix.Length..])
            .Build());
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
