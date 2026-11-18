using Consent.Api.Infrastructure;
using Consent.Application.Abstractions;
using Consent.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consent.Api.Controllers;

/// <summary>Phản hồi đăng nhập/làm mới. Refresh token không nằm trong body mà trong cookie HttpOnly.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, CurrentUserDto User)
{
    public static AuthResponse From(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new AuthResponse(session.AccessToken, session.AccessTokenExpiresAtUtc, session.User);
    }
}

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser, IHostEnvironment environment) : ControllerBase
{
    /// <summary>Đăng nhập bằng tên đăng nhập và mật khẩu.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, StartSession);
    }

    /// <summary>Làm mới access token bằng refresh token trong cookie (xoay vòng refresh token).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [RequireRequestedWithHeader]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(RefreshTokenCookie.Read(HttpContext), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure && result.Error != AuthErrors.RefreshSuperseded)
        {
            RefreshTokenCookie.Clear(HttpContext, environment);
        }

        return this.ToActionResult(result, StartSession);
    }

    /// <summary>Đăng xuất: thu hồi refresh token của thiết bị hiện tại và xóa cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [RequireRequestedWithHeader]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(RefreshTokenCookie.Read(HttpContext), cancellationToken).ConfigureAwait(false);
        RefreshTokenCookie.Clear(HttpContext, environment);
        return NoContent();
    }

    /// <summary>Thông tin người dùng đang đăng nhập, gồm vai trò và quyền.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await auth.GetCurrentUserAsync(currentUser.UserId!.Value, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result);
    }

    /// <summary>Đổi mật khẩu của chính mình. Các phiên đăng nhập khác bị thu hồi.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.ChangePasswordAsync(currentUser.UserId!.Value, request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, StartSession);
    }

    private OkObjectResult StartSession(AuthSession session)
    {
        RefreshTokenCookie.Write(HttpContext, session.RefreshToken, session.RefreshTokenExpiresAtUtc, environment);
        return Ok(AuthResponse.From(session));
    }
}
