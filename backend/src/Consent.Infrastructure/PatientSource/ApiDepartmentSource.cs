using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.PatientSource;

/// <summary>Chưa cấu hình API thông tin người bệnh: danh mục khoa/phòng thêm, sửa trong phần mềm như trước.</summary>
internal sealed class DisabledDepartmentSource : IDepartmentSource
{
    public bool IsEnabled => false;

    public Task<DepartmentSourceResult> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(DepartmentSourceResult.Disabled);
}

/// <summary>
/// Gọi <c>GET api/v1/departments</c> của API thông tin người bệnh (quyết định 42). API chưa cấu hình câu truy vấn (501) trả
/// về <see cref="DepartmentSourceStatus.NotConfigured"/>; lỗi kết nối, quá thời gian, mã lỗi khác hay phản hồi lạ trả về
/// <see cref="DepartmentSourceStatus.Unavailable"/>.
/// </summary>
internal sealed partial class ApiDepartmentSource(
    PatientSourceHttp http, IOptions<PatientSourceOptions> options, ILogger<ApiDepartmentSource> logger)
    : IDepartmentSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public async Task<DepartmentSourceResult> GetAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl!), "api/v1/departments"));
        request.Headers.Add("X-Api-Key", settings.ApiKey);

        try
        {
            using var response = await http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotImplemented)
            {
                return DepartmentSourceResult.NotConfigured;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return DepartmentSourceResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<DepartmentsResponse>(Json, cancellationToken).ConfigureAwait(false);
            if (body?.Items is not { } items)
            {
                return DepartmentSourceResult.Unavailable;
            }

            List<ExternalDepartment> departments =
            [
                .. items
                    .Where(i => !string.IsNullOrWhiteSpace(i.Code))
                    .Select(i => new ExternalDepartment(i.Code!.Trim(), string.IsNullOrWhiteSpace(i.Name) ? i.Code.Trim() : i.Name.Trim())),
            ];
            return new DepartmentSourceResult(DepartmentSourceStatus.Found, departments);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, ex.GetType().Name);
            return DepartmentSourceResult.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API thông tin người bệnh trả mã {StatusCode} khi lấy danh mục khoa/phòng.")]
    private static partial void LogFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không gọi được API thông tin người bệnh để lấy danh mục khoa/phòng ({Error}).")]
    private static partial void LogUnreachable(ILogger logger, string error);

    private sealed record DepartmentItem(string? Code, string? Name);

    private sealed record DepartmentsResponse(IReadOnlyList<DepartmentItem>? Items);
}
