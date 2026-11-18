using System.Net.Http.Json;
using System.Text.Json;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.PatientSource;

/// <summary>Chưa cấu hình API thông tin người bệnh: không lấy được số liệu HIS cho bảng tổng hợp điều dưỡng.</summary>
internal sealed class DisabledDailyStatisticsSource : IDailyStatisticsSource
{
    public bool IsEnabled => false;

    public Task<DailyStatisticsResult> GetAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(DailyStatisticsResult.Disabled);
}

/// <summary>
/// Gọi <c>POST api/v1/daily-statistics</c> của API thông tin người bệnh (quyết định 31). Lỗi kết nối, quá thời gian, chưa
/// cấu hình câu truy vấn hay phản hồi lạ đều trả về <see cref="DailyStatisticsSourceStatus.Unavailable"/>.
/// </summary>
internal sealed partial class ApiDailyStatisticsSource(
    PatientSourceHttp http, IOptions<PatientSourceOptions> options, ILogger<ApiDailyStatisticsSource> logger)
    : IDailyStatisticsSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public async Task<DailyStatisticsResult> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl!), "api/v1/daily-statistics"))
        {
            Content = JsonContent.Create(new StatisticsRequest(date), options: Json),
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);

        try
        {
            using var response = await http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return DailyStatisticsResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<StatisticsResponse>(Json, cancellationToken).ConfigureAwait(false);
            if (body?.Items is not { } items)
            {
                return DailyStatisticsResult.Unavailable;
            }

            List<ExternalDailyStatistic> statistics =
            [
                .. items
                    .Where(i => !string.IsNullOrWhiteSpace(i.Indicator) && i.Value is >= 0)
                    .Select(i => new ExternalDailyStatistic(
                        i.Indicator!.Trim(), string.IsNullOrWhiteSpace(i.DepartmentCode) ? null : i.DepartmentCode.Trim(), i.Value!.Value)),
            ];
            return new DailyStatisticsResult(DailyStatisticsSourceStatus.Found, statistics);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, ex.GetType().Name);
            return DailyStatisticsResult.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API thông tin người bệnh trả mã {StatusCode} khi lấy số liệu trong ngày.")]
    private static partial void LogFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không gọi được API thông tin người bệnh để lấy số liệu trong ngày ({Error}).")]
    private static partial void LogUnreachable(ILogger logger, string error);

    private sealed record StatisticsRequest(DateOnly Date);

    private sealed record StatisticsItem(string? Indicator, string? DepartmentCode, int? Value);

    private sealed record StatisticsResponse(IReadOnlyList<StatisticsItem>? Items);
}
