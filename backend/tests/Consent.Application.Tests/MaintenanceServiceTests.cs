using System.Text.Json;
using Consent.Application.Auth;
using Consent.Application.Maintenance;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.Identity;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Chế độ bảo trì (quyết định 53): bật, sửa, tắt; trạng thái giữ trong bộ nhớ; đăng nhập và làm mới phiên khi đang bảo trì.</summary>
public sealed class MaintenanceServiceTests
{
    private const string Password = AppTestContext.DefaultPassword;

    private static AppTestContext WithAdministrator(out User admin)
    {
        var ctx = new AppTestContext();
        admin = ctx.AddUser("quantri", SystemRoles.Administrator);
        ctx.CurrentUser.UserId = admin.Id;
        ctx.CurrentUser.UserName = admin.UserName;
        return ctx;
    }

    private static string[] MaintenanceKeys(AppTestContext ctx) =>
        [.. ctx.Store.SystemSettings.Where(s => s.Key.StartsWith("Maintenance.", StringComparison.Ordinal)).Select(s => s.Key).Order(StringComparer.Ordinal)];

    private static Task<Result<MaintenanceSettingsDto>> EnableAsync(
        AppTestContext ctx, string reason = "Nâng cấp phần mềm", DateTimeOffset? expectedEnd = null) =>
        ctx.CreateMaintenanceService().UpdateAsync(new UpdateMaintenanceRequest(true, reason, expectedEnd), CancellationToken.None);

    [Fact]
    public async Task ByDefault_NotUnderMaintenance()
    {
        var ctx = new AppTestContext();

        var status = await ctx.CreateMaintenanceService().GetStatusAsync(CancellationToken.None);

        Assert.Equal(new MaintenanceStatusDto(false, null, null), status);
    }

    [Fact]
    public async Task Enable_StoresReasonExpectedEndStartAndWho_AuditsOnce_AndStatusShowsOnlyReasonAndEnd()
    {
        var ctx = WithAdministrator(out var admin);
        var end = ctx.Clock.UtcNow.AddHours(2).ToOffset(TimeSpan.FromHours(7));

        var result = await EnableAsync(ctx, "  Nâng cấp phần mềm,\r\n cập nhật   mẫu phiếu  ", end);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var settings = result.Value;
        Assert.True(settings.Enabled);
        Assert.Equal("Nâng cấp phần mềm, cập nhật mẫu phiếu", settings.Reason);
        Assert.Equal(end, settings.ExpectedEndAtUtc);
        Assert.Equal(TimeSpan.Zero, settings.ExpectedEndAtUtc!.Value.Offset);
        Assert.Equal(ctx.Clock.UtcNow, settings.StartedAtUtc);
        Assert.Equal($"{admin.FullName} (quantri)", settings.StartedBy);
        Assert.Equal(
            [SettingKeys.MaintenanceExpectedEndAtUtc, SettingKeys.MaintenanceReason, SettingKeys.MaintenanceStartedAtUtc, SettingKeys.MaintenanceStartedBy],
            MaintenanceKeys(ctx));
        var audit = Assert.Single(ctx.Store.AuditLogs);
        Assert.Equal(AuditActions.MaintenanceEnabled, audit.Action);
        Assert.Equal(admin.Id, audit.UserId);
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal("Nâng cấp phần mềm, cập nhật mẫu phiếu", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(1, ctx.Store.SaveCount);

        // Đọc lại từ kho (dịch vụ khác, như một yêu cầu khác) và trạng thái công khai không có ai bật, lúc nào bật.
        Assert.Equal(settings, await ctx.CreateMaintenanceService().GetSettingsAsync(CancellationToken.None));
        Assert.Equal(
            new MaintenanceStatusDto(true, "Nâng cấp phần mềm, cập nhật mẫu phiếu", end),
            await ctx.CreateMaintenanceService().GetStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Enable_WithoutExpectedEnd_IsAllowed()
    {
        var ctx = WithAdministrator(out _);

        var result = await EnableAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExpectedEndAtUtc);
        Assert.DoesNotContain(SettingKeys.MaintenanceExpectedEndAtUtc, MaintenanceKeys(ctx));
    }

    [Theory]
    [InlineData(null, "reason")]
    [InlineData("  \r\n\t ", "reason")]
    public async Task Enable_WithoutReason_IsRejected_AndNothingChanges(string? reason, string field)
    {
        var ctx = WithAdministrator(out _);

        var result = await EnableAsync(ctx, reason!);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains(field, result.Error.FieldErrors!.Keys);
        Assert.Empty(MaintenanceKeys(ctx));
        Assert.Empty(ctx.Store.AuditLogs);
        Assert.False((await ctx.CreateMaintenanceService().GetStatusAsync(CancellationToken.None)).Enabled);
    }

    [Fact]
    public async Task Enable_ReasonLongerThanLimit_IsRejected_ButExactlyTheLimitIsAccepted()
    {
        var ctx = WithAdministrator(out _);

        var tooLong = await EnableAsync(ctx, new string('a', MaintenanceService.ReasonMaxLength + 1));
        var limit = await EnableAsync(ctx, new string('a', MaintenanceService.ReasonMaxLength));

        Assert.Contains("reason", tooLong.Error!.FieldErrors!.Keys);
        Assert.True(limit.IsSuccess);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(30 * 24 * 60 + 1)]
    public async Task Enable_ExpectedEndNotInTheNext30Days_IsRejected(int minutesFromNow)
    {
        var ctx = WithAdministrator(out _);

        var result = await EnableAsync(ctx, expectedEnd: ctx.Clock.UtcNow.AddMinutes(minutesFromNow));

        Assert.Contains("expectedEndAtUtc", result.Error!.FieldErrors!.Keys);
        Assert.Empty(MaintenanceKeys(ctx));
    }

    [Fact]
    public async Task Disable_WithInvalidReasonOrEnd_IsStillAllowed()
    {
        var ctx = WithAdministrator(out _);
        await EnableAsync(ctx);

        var result = await ctx.CreateMaintenanceService().UpdateAsync(
            new UpdateMaintenanceRequest(false, null, ctx.Clock.UtcNow.AddYears(-1)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Enabled);
    }

    [Fact]
    public async Task UpdateWhileEnabled_ChangesReasonAndEnd_KeepsStartAndWho_AuditsUpdated()
    {
        var ctx = WithAdministrator(out var admin);
        var started = ctx.Clock.UtcNow;
        await EnableAsync(ctx, "Nâng cấp", ctx.Clock.UtcNow.AddHours(1));
        var other = ctx.AddUser("quantri2", SystemRoles.Administrator);
        ctx.CurrentUser.UserId = other.Id;
        ctx.CurrentUser.UserName = other.UserName;
        ctx.Clock.Advance(TimeSpan.FromMinutes(50));

        var later = await EnableAsync(ctx, "Nâng cấp, chậm hơn dự kiến", ctx.Clock.UtcNow.AddHours(1));
        var withoutEnd = await EnableAsync(ctx, "Nâng cấp, chậm hơn dự kiến");

        Assert.Equal(started, later.Value.StartedAtUtc);
        Assert.Equal($"{admin.FullName} (quantri)", later.Value.StartedBy);
        Assert.Equal("Nâng cấp, chậm hơn dự kiến", later.Value.Reason);
        Assert.Null(withoutEnd.Value.ExpectedEndAtUtc);
        Assert.DoesNotContain(SettingKeys.MaintenanceExpectedEndAtUtc, MaintenanceKeys(ctx));
        Assert.Equal(
            [AuditActions.MaintenanceEnabled, AuditActions.MaintenanceUpdated, AuditActions.MaintenanceUpdated],
            ctx.Store.AuditLogs.Select(a => a.Action));
        Assert.Equal(other.Id, ctx.Store.AuditLogs[^1].UserId);
    }

    [Fact]
    public async Task SavingTheSameValues_ChangesNothing_AndIsNotAudited()
    {
        var ctx = WithAdministrator(out _);
        var end = ctx.Clock.UtcNow.AddHours(1);
        await EnableAsync(ctx, "Nâng cấp", end);

        var again = await EnableAsync(ctx, " Nâng cấp ", end.ToOffset(TimeSpan.FromHours(7)));

        Assert.True(again.IsSuccess);
        Assert.Single(ctx.Store.AuditLogs);
        Assert.Equal(1, ctx.Store.SaveCount);
    }

    [Fact]
    public async Task Disable_RemovesEveryMaintenanceRow_KeepsOtherSettings_AuditsWithStart_AndDisablingAgainDoesNothing()
    {
        var ctx = WithAdministrator(out _);
        ctx.SetInheritanceDays(7);
        var started = ctx.Clock.UtcNow;
        await EnableAsync(ctx, "Nâng cấp", ctx.Clock.UtcNow.AddHours(1));
        ctx.Clock.Advance(TimeSpan.FromMinutes(30));
        var service = ctx.CreateMaintenanceService();

        var off = await service.UpdateAsync(new UpdateMaintenanceRequest(false, null, null), CancellationToken.None);
        var again = await service.UpdateAsync(new UpdateMaintenanceRequest(false, null, null), CancellationToken.None);

        Assert.Equal(new MaintenanceSettingsDto(false, null, null, null, null), off.Value);
        Assert.Equal(off.Value, again.Value);
        Assert.Empty(MaintenanceKeys(ctx));
        Assert.Contains(ctx.Store.SystemSettings, s => s.Key == SettingKeys.FormInheritanceDays);
        Assert.Equal([AuditActions.MaintenanceEnabled, AuditActions.MaintenanceDisabled], ctx.Store.AuditLogs.Select(a => a.Action));
        using var details = JsonDocument.Parse(ctx.Store.AuditLogs[^1].Details!);
        Assert.Equal(started, details.RootElement.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.False((await ctx.CreateMaintenanceService().GetStatusAsync(CancellationToken.None)).Enabled);
    }

    [Fact]
    public async Task OnlyTheReasonRowMeansUnderMaintenance_OtherRowsAreOptional_AndBadTimesAreIgnored()
    {
        var ctx = new AppTestContext();
        ctx.Store.SystemSettings.Add(SystemSetting.Create(SettingKeys.MaintenanceStartedAtUtc, "khong-phai-thoi-gian", ctx.Clock.UtcNow));
        var service = ctx.CreateMaintenanceService();

        Assert.False((await service.GetStatusAsync(CancellationToken.None)).Enabled);

        ctx.Store.SystemSettings.Add(SystemSetting.Create(SettingKeys.MaintenanceReason, "Sửa CSDL", ctx.Clock.UtcNow));
        var settings = await ctx.CreateMaintenanceService().GetSettingsAsync(CancellationToken.None);

        Assert.Equal(new MaintenanceSettingsDto(true, "Sửa CSDL", null, null, null), settings);
    }

    [Fact]
    public async Task State_IsKeptInMemoryForTheCacheLifetime_ButChangesThroughTheServiceApplyAtOnce()
    {
        var ctx = WithAdministrator(out _);
        ctx.MaintenanceCache = new MaintenanceStateCache(TimeSpan.FromSeconds(5));
        Assert.False((await ctx.CreateMaintenanceService().GetStateAsync(CancellationToken.None)).Enabled);

        // Bật qua dịch vụ: mọi yêu cầu sau đó (dịch vụ khác, cùng bộ nhớ) thấy ngay.
        await EnableAsync(ctx);
        Assert.True((await ctx.CreateMaintenanceService().GetStateAsync(CancellationToken.None)).Enabled);

        // Tắt thẳng trong CSDL (tắt khẩn cấp bằng SQL): có hiệu lực khi hết thời gian giữ trong bộ nhớ.
        ctx.Store.SystemSettings.RemoveAll(s => s.Key.StartsWith("Maintenance.", StringComparison.Ordinal));
        ctx.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.True((await ctx.CreateMaintenanceService().GetStateAsync(CancellationToken.None)).Enabled);
        ctx.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False((await ctx.CreateMaintenanceService().GetStateAsync(CancellationToken.None)).Enabled);
    }

    [Fact]
    public void TheApplicationKeepsTheStateInMemoryForFiveSeconds_SharedByAllRequests()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddApplication();

        var registration = Assert.Single(services, d => d.ServiceType == typeof(MaintenanceStateCache));
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, registration.Lifetime);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.IsType<MaintenanceStateCache>(registration.ImplementationInstance).Lifetime);
    }

    [Fact]
    public void CacheEntryFromTheFuture_IsNotUsed()
    {
        var cache = new MaintenanceStateCache(TimeSpan.FromSeconds(5));
        var now = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        cache.Set(MaintenanceState.Off, now);

        Assert.True(cache.TryGet(now.AddSeconds(4), out _));
        Assert.False(cache.TryGet(now.AddSeconds(5), out _));
        Assert.False(cache.TryGet(now.AddSeconds(-1), out _));
    }

    [Fact]
    public async Task UnderMaintenance_CorrectPasswordWithoutPermission_IsRefusedWith503_AuditedAndNoSession()
    {
        var ctx = WithAdministrator(out _);
        var doctor = ctx.AddUser("bacsi");
        var end = ctx.Clock.UtcNow.AddHours(1);
        await EnableAsync(ctx, "Nâng cấp", end);
        var auth = ctx.CreateAuthService();

        var result = await auth.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None);

        Assert.Equal(ErrorType.Unavailable, result.Error!.Type);
        Assert.Equal(MaintenanceService.ErrorCode, result.Error.Code);
        Assert.Equal(AuthService.MaintenanceLoginMessage, result.Error.Message);
        Assert.Equal("Nâng cấp", result.Error.Metadata!["reason"]);
        Assert.Equal(end, result.Error.Metadata["expectedEndAtUtc"]);
        Assert.Empty(ctx.Store.RefreshTokens);
        Assert.Null(doctor.LastLoginAtUtc);
        var audit = ctx.Store.AuditLogs[^1];
        Assert.Equal(AuditActions.LoginFailed, audit.Action);
        Assert.Equal(doctor.Id, audit.UserId);
        Assert.Contains("maintenance", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderMaintenance_WrongPassword_StillSaysInvalidCredentials_AndCountsTowardsLockout()
    {
        var ctx = WithAdministrator(out _);
        var doctor = ctx.AddUser("bacsi");
        await EnableAsync(ctx);

        var result = await ctx.CreateAuthService().LoginAsync(new LoginRequest("bacsi", "sai-mat-khau"), CancellationToken.None);

        Assert.Equal(AuthErrors.InvalidCredentials, result.Error);
        Assert.Equal(1, doctor.FailedLoginCount);
    }

    [Fact]
    public async Task UnderMaintenance_AdministratorAndRolesGrantedMaintenance_LogIn_ButAnAdministratorWhoMustChangePasswordCannot()
    {
        var ctx = WithAdministrator(out _);
        var itRole = Role.Create("IT", "Công nghệ thông tin", null, isSystem: false);
        itRole.SetPermissions([Permissions.SystemMaintenance, Permissions.SettingsManage]);
        ctx.Store.Roles.Add(itRole);
        var it = ctx.AddUser("cntt", SystemRoles.Doctor);
        it.SetRoles([itRole.Id]);
        ctx.AddUser("quantrimoi", SystemRoles.Administrator, mustChangePassword: true);
        await EnableAsync(ctx);
        var auth = ctx.CreateAuthService();

        Assert.True((await auth.LoginAsync(new LoginRequest("quantri", Password), CancellationToken.None)).IsSuccess);
        Assert.True((await auth.LoginAsync(new LoginRequest("cntt", Password), CancellationToken.None)).IsSuccess);
        Assert.Equal(
            MaintenanceService.ErrorCode,
            (await auth.LoginAsync(new LoginRequest("quantrimoi", Password), CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task UnderMaintenance_RefreshWithoutPermission_IsRefused_WithoutRevokingOrRotating_AndWorksAfterMaintenance()
    {
        var ctx = WithAdministrator(out _);
        ctx.AddUser("bacsi");
        var auth = ctx.CreateAuthService();
        var login = (await auth.LoginAsync(new LoginRequest("bacsi", Password), CancellationToken.None)).Value;
        await EnableAsync(ctx);

        var refused = await auth.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.Equal(MaintenanceService.ErrorCode, refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
        var token = Assert.Single(ctx.Store.RefreshTokens);
        Assert.True(token.IsActive(ctx.Clock.UtcNow));
        Assert.False(token.WasRotated);

        await ctx.CreateMaintenanceService().UpdateAsync(new UpdateMaintenanceRequest(false, null, null), CancellationToken.None);
        var resumed = await auth.RefreshAsync(login.RefreshToken, CancellationToken.None);

        Assert.True(resumed.IsSuccess);
        Assert.True(token.WasRotated);
    }

    [Fact]
    public async Task UnderMaintenance_AdministratorRefreshes_AndAnInvalidTokenIsStillInvalid()
    {
        var ctx = WithAdministrator(out _);
        var auth = ctx.CreateAuthService();
        var login = (await auth.LoginAsync(new LoginRequest("quantri", Password), CancellationToken.None)).Value;
        await EnableAsync(ctx);

        Assert.True((await auth.RefreshAsync(login.RefreshToken, CancellationToken.None)).IsSuccess);
        Assert.Equal(AuthErrors.RefreshInvalid, (await auth.RefreshAsync("khong-ton-tai", CancellationToken.None)).Error);
    }

    [Fact]
    public void MaintenancePermission_IsInTheCatalog_InTheSystemGroup()
    {
        var definition = Assert.Single(PermissionCatalog.All, p => p.Code == Permissions.SystemMaintenance);

        Assert.Equal("Hệ thống", definition.Group);
        Assert.True(MaintenanceService.CanEnterDuringMaintenance([Permissions.SystemMaintenance]));
        Assert.False(MaintenanceService.CanEnterDuringMaintenance(PermissionCatalog.AllCodes.Where(c => c != Permissions.SystemMaintenance)));
    }
}
