using Consent.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Nhãn môi trường (quyết định 50): môi trường kiểm thử (UAT) đặt <c>App:EnvironmentLabel</c>, giao diện hiện dải báo
/// "môi trường kiểm thử" trên mọi trang, kể cả trang đăng nhập, để người dùng không nhầm với hệ thống chạy thật. Môi trường
/// chạy thật (Production) không đặt nhãn. Không cần đăng nhập; chỉ trả nhãn, không có cấu hình nào khác.
/// </summary>
[ApiController]
[Route("api/v1/environment")]
[AllowAnonymous]
[AllowDuringMaintenance]
[Produces("application/json")]
public sealed class EnvironmentController(IConfiguration configuration) : ControllerBase
{
    /// <summary>Độ dài tối đa của nhãn hiện trên giao diện.</summary>
    public const int MaxLabelLength = 30;

    [HttpGet]
    public EnvironmentResponse Get() => new(Label(configuration["App:EnvironmentLabel"]));

    /// <summary>Nhãn đã làm sạch: bỏ ký tự điều khiển và khoảng trắng hai đầu, tối đa 30 ký tự; trống thì <c>null</c>.</summary>
    internal static string? Label(string? value)
    {
        var text = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= MaxLabelLength ? text : text[..MaxLabelLength].TrimEnd();
    }
}

/// <param name="Label">Nhãn môi trường (ví dụ "UAT"); <c>null</c> ở môi trường chạy thật.</param>
public sealed record EnvironmentResponse(string? Label);
