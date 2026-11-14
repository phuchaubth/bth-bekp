using Consent.Application.Auth;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class AuthServiceTests
{
    private const string Password = AppTestContext.DefaultPassword;

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSession_StoresHashedRefreshToken_AndAudits()
    {
        var ctx = new AppTestContext();
        var department = ctx.AddDepartment("NOI", "Khoa Nội");
        var user = ctx.AddUser("bacsi", departmentId: department.Id);
        var service = ctx.CreateAuthService();

        var result = await service.LoginAsync(new LoginRequest(" BACSI ", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var session = result.Value;
        Assert.Equal("Khoa Nội", session.User.DepartmentName);
        Assert.Contains(Permissions.ConsentsCreate, session.User.Permissions);
        Assert.DoesNotContain(Permissions.UsersManage, session.User.Permissions);

        var stored = Assert.Single(ctx.Store.RefreshTokens);
        Assert.NotEqual(session.RefreshToken, stored.TokenHash);
        Assert.Equal(ctx.Tokens.HashRefreshToken(session.RefreshToken), stored.TokenHash);
        Assert.Equal(ctx.Clock.UtcNow, user.LastLoginAtUtc);
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.LoginSucceeded && a.UserId == user.Id);
    }

    [Fact]
    public async Task Login_UnknownUser_ReturnsInvalidCredentials_AndAuditsAttempt()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateAuthService();

        var result = await service.LoginAsync(new LoginRequest("khong-ton-tai", Password), CancellationToken.None);

        Assert.Equal(AuthErrors.InvalidCredentials, result.Error);
        var audit = Assert.Single(ctx.Store.AuditLogs);
        Assert.Equal(AuditActions.LoginFailed, audit.Action);
        Assert.Null(audit.UserId);
        Assert.Contains("unknown_user", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveWrongPasswords_LockAccount_AndCorrectPasswordIsThenRejected()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("tiepdon");
        var service = ctx.CreateAuthService();

        Error? lastError = null;
        for (var i = 0; i < 5; i++)
        {
            lastError = (await service.LoginAsync(new LoginRequest("tiepdon", "Sai-Mat-Khau-1"), CancellationToken.None)).Error;
        }

        Assert.Equal("auth.locked_out", lastError!.Code);
        Assert.True(user.IsLockedOut(ctx.Clock.UtcNow));
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.LockedOut);

        var correct = await service.LoginAsync(new LoginRequest("tiepdon", Password), CancellationToken.None);
        Assert.Equal("auth.locked_out", correct.Error!.Code);
        Assert.Empty(ctx.Store.RefreshTokens);
    }

    [Fact]
    public async Task Login_AfterLockoutExpires_Succeeds()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("tiepdon");
        var service = ctx.CreateAuthService();
        for (var i = 0; i < 5; i++)
        {
            await service.LoginAsync(new LoginRequest("tiepdon", "Sai-Mat-Khau-1"), CancellationToken.None);
        }

        ctx.Clock.Advance(TimeSpan.FromMinutes(ctx.Options.LockoutMinutes) + TimeSpan.FromSeconds(1));

        var result = await service.LoginAsync(new LoginRequest("tiepdon", Password), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task InactiveUser_IsOnlyToldAboutLock_WhenPasswordIsCorrect()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("nghiviec", active: false);
        var service = ctx.CreateAuthService();

        var wrong = await service.LoginAsync(new LoginRequest("nghiviec", "Sai-Mat-Khau-1"), CancellationToken.None);
        var right = await service.LoginAsync(new LoginRequest("nghiviec", Password), CancellationToken.None);

        Assert.Equal(AuthErrors.InvalidCredentials, wrong.Error);
        Assert.Equal(AuthErrors.Inactive, right.Error);
        Assert.Empty(ctx.Store.RefreshTokens);
    }

    [Fact]
    public async Task Login_WhenPasswordMustBeChanged_IssuesTokenWithoutPermissions()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("moi", role: SystemRoles.Administrator, mustChangePassword: true);
        var service = ctx.CreateAuthService();

        var result = await service.LoginAsync(new LoginRequest("moi", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.User.MustChangePassword);
        Assert.Empty(result.Value.User.Permissions);
        Assert.Empty(ctx.Tokens.LastClaims!.Permissions);
        Assert.True(ctx.Tokens.LastClaims.MustChangePassword);
    }

    [Fact]
    public async Task Login_WithLegacyHash_RehashesPassword()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("cu.user");
        user.SetPassword("legacy:" + Password, mustChangePassword: false);
        var service = ctx.CreateAuthService();

        var result = await service.LoginAsync(new LoginRequest("cu.user", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:" + Password, user.PasswordHash);
    }

    [Fact]
    public async Task Login_WithEmptyFields_ReturnsFieldErrors()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateAuthService();

        var result = await service.LoginAsync(new LoginRequest("", ""), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("userName", result.Error.FieldErrors!.Keys);
        Assert.Contains("password", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task Refresh_RotatesToken_InSameFamily()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;

        var refreshed = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(login.RefreshToken, refreshed.Value.RefreshToken);
        var original = ctx.Store.RefreshTokens[0];
        var replacement = ctx.Store.RefreshTokens[1];
        Assert.True(original.WasRotated);
        Assert.Equal(replacement.Id, original.ReplacedByTokenId);
        Assert.Equal(original.FamilyId, replacement.FamilyId);
        Assert.True(replacement.IsActive(ctx.Clock.UtcNow));
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_WithinGrace_IsRejectedWithoutRevokingFamily()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;
        var refreshed = (await service.RefreshAsync(login.RefreshToken, CancellationToken.None)).Value;

        var reuse = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Equal(AuthErrors.RefreshSuperseded, reuse.Error);
        Assert.True((await service.RefreshAsync(refreshed.RefreshToken, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_AfterGrace_RevokesWholeFamily_AndAudits()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;
        var refreshed = (await service.RefreshAsync(login.RefreshToken, CancellationToken.None)).Value;
        ctx.Clock.Advance(TimeSpan.FromSeconds(ctx.Options.RefreshReuseGraceSeconds + 1));

        var reuse = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Equal(AuthErrors.RefreshInvalid, reuse.Error);
        Assert.All(ctx.Store.RefreshTokens, t => Assert.True(t.IsRevoked));
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.RefreshTokenReuseDetected);
        Assert.Equal(AuthErrors.RefreshInvalid, (await service.RefreshAsync(refreshed.RefreshToken, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_IsRejected()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;
        ctx.Clock.Advance(TimeSpan.FromHours(ctx.Options.RefreshTokenLifetimeHours));

        var result = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Equal(AuthErrors.RefreshInvalid, result.Error);
    }

    [Fact]
    public async Task Refresh_ForDeactivatedUser_IsRejected_AndFamilyRevoked()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;
        user.Deactivate();

        var result = await service.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Equal(AuthErrors.Inactive, result.Error);
        Assert.All(ctx.Store.RefreshTokens, t => Assert.True(t.IsRevoked));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khong-ton-tai")]
    public async Task Refresh_WithMissingOrUnknownToken_IsRejected(string? token)
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateAuthService();

        var result = await service.RefreshAsync(token, CancellationToken.None);

        Assert.Equal(AuthErrors.RefreshInvalid, result.Error);
    }

    [Fact]
    public async Task Logout_RevokesToken_AndAudits()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();
        var login = (await service.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;

        await service.LogoutAsync(login.RefreshToken, CancellationToken.None);

        Assert.True(ctx.Store.RefreshTokens.Single().IsRevoked);
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.Logout);
        Assert.Equal(AuthErrors.RefreshInvalid, (await service.RefreshAsync(login.RefreshToken, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsFieldError()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();

        var result = await service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest("Sai-Mat-Khau-1", "Mat-Khau-Khac-2026"), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("currentPassword", result.Error.FieldErrors!.Keys);
    }

    [Fact]
    public async Task ChangePassword_ContainingUserName_IsRejected()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("bacsi");
        var service = ctx.CreateAuthService();

        var result = await service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest(Password, "Bacsi-Moi-2026!"), CancellationToken.None);

        Assert.Contains("newPassword", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task ChangePassword_Succeeds_RevokesOtherSessions_AndGrantsPermissions()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("moi", mustChangePassword: true);
        var service = ctx.CreateAuthService();
        var first = (await service.LoginAsync(new LoginRequest("moi", Password), CancellationToken.None)).Value;

        var result = await service.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest(Password, "Mat-Khau-Khac-2026"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.MustChangePassword);
        Assert.Equal("hashed:Mat-Khau-Khac-2026", user.PasswordHash);
        Assert.NotEmpty(result.Value.User.Permissions);
        Assert.Equal(AuthErrors.RefreshInvalid, (await service.RefreshAsync(first.RefreshToken, CancellationToken.None)).Error);
        var refreshed = await service.RefreshAsync(result.Value.RefreshToken, CancellationToken.None);
        Assert.True(refreshed.IsSuccess, refreshed.Error?.Code);
        Assert.Contains(ctx.Store.AuditLogs, a => a.Action == AuditActions.PasswordChanged);
    }

    [Fact]
    public async Task GetCurrentUser_ForDeactivatedUser_ReturnsSessionInvalid()
    {
        var ctx = new AppTestContext();
        var user = ctx.AddUser("bacsi", active: false);
        var service = ctx.CreateAuthService();

        var result = await service.GetCurrentUserAsync(user.Id, CancellationToken.None);

        Assert.Equal(AuthErrors.SessionInvalid, result.Error);
    }
}
