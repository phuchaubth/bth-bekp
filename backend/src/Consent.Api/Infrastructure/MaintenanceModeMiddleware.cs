using System.Globalization;
using Consent.Api.Authorization;
using Consent.Application.Maintenance;
using Consent.Domain.Authorization;
using Consent.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Consent.Api.Infrastructure;

/// <summary>
/// Endpoint vẫn trả lời mọi người khi đang bảo trì (quyết định 53): kiểm tra sức khỏe, nhãn môi trường, trạng thái bảo trì, đăng
/// nhập, làm mới phiên, đăng xuất. Đăng nhập và làm mới phiên tự chặn tài khoản không có quyền bảo trì.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowDuringMaintenanceAttribute : Attribute;

/// <summary>
/// Chế độ bảo trì (quyết định 53): đang bảo trì thì trả 503 (mã <see cref="MaintenanceService.ErrorCode"/>, kèm lý do và giờ dự
/// kiến xong) cho mọi yêu cầu của người không có quyền <see cref="Permissions.SystemMaintenance"/>, kể cả kiosk và người bệnh ký
/// trên máy tính bảng hoặc qua mã QR, trừ endpoint gắn <see cref="AllowDuringMaintenanceAttribute"/>. Chạy sau xác thực (đã biết
/// quyền trong token), trước phân quyền. Token đăng nhập hết hạn hoặc sai thì trả 401 như khi không bảo trì, để giao diện làm mới
/// phiên (làm mới phiên tự quyết cho vào hay không).
/// </summary>
internal sealed class MaintenanceModeMiddleware(RequestDelegate next)
{
    /// <summary>Gợi ý cho máy khách: hỏi lại sau bấy nhiêu giây.</summary>
    public const int RetryAfterSeconds = 30;

    public const string Message = "Hệ thống đang bảo trì. Vui lòng quay lại sau.";

    public async Task InvokeAsync(HttpContext context, MaintenanceService maintenance)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(maintenance);
        if (context.GetEndpoint()?.Metadata.GetMetadata<AllowDuringMaintenanceAttribute>() is not null
            || context.User.HasClaim(AppClaimTypes.Permission, Permissions.SystemMaintenance))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var state = await maintenance.GetStateAsync(context.RequestAborted).ConfigureAwait(false);
        if (!state.Enabled)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (await HasRejectedLoginTokenAsync(context).ConfigureAwait(false))
        {
            await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        var details = ProblemDetailsMapping.ToProblemDetails(MaintenanceService.Blocked(state, Message), context);
        await TypedResults.Problem(details).ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi token Bearer nhưng không phải token đăng nhập hợp lệ (hết hạn, sai chữ ký…) và cũng không phải token phiên ký hợp lệ:
    /// trả 401 để giao diện làm mới phiên. Token phiên ký hợp lệ (người bệnh đang ký) thì vẫn chặn bằng 503.
    /// </summary>
    private static async Task<bool> HasRejectedLoginTokenAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            || !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var signing = await context.AuthenticateAsync(SigningSession.AuthenticationScheme).ConfigureAwait(false);
        return !signing.Succeeded;
    }
}
