using System.Net.Http.Json;
using System.Text.Json;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.PatientSource;

/// <summary>Chưa bật gắn lượt tiếp nhận (<c>PatientSource:EncountersEnabled</c> = false).</summary>
internal sealed class DisabledEncounterSource : IEncounterSource
{
    public bool IsEnabled => false;

    public Task<EncounterSourceResult> FindAsync(
        string? patientCode, string? citizenId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(EncounterSourceResult.Disabled);
}

/// <summary>
/// Gọi <c>POST api/v1/encounters/search</c> của API thông tin người bệnh (quyết định 25; mã/CCCD đi trong thân, không nằm trên
/// URL). Lỗi kết nối, quá thời gian, chưa cấu hình truy vấn hay phản hồi lạ đều trả về
/// <see cref="EncounterSourceStatus.Unavailable"/>.
/// </summary>
internal sealed partial class ApiEncounterSource(PatientSourceHttp http, IOptions<PatientSourceOptions> options, ILogger<ApiEncounterSource> logger)
    : IEncounterSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public async Task<EncounterSourceResult> FindAsync(
        string? patientCode, string? citizenId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl!), "api/v1/encounters/search"))
        {
            Content = JsonContent.Create(new SearchRequest(patientCode, citizenId, from, to), options: Json),
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);

        try
        {
            using var response = await http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return EncounterSourceResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<SearchResponse>(Json, cancellationToken).ConfigureAwait(false);
            if (body?.Items is not { } items)
            {
                return EncounterSourceResult.Unavailable;
            }

            List<ExternalEncounter> encounters =
            [
                .. items
                    .Where(i => !string.IsNullOrWhiteSpace(i.EncounterNo) && i.ReceivedAt is not null)
                    .Select(i => new ExternalEncounter(i.EncounterNo!.Trim(), i.ReceivedAt!.Value, Clean(i.Place), Clean(i.ReceivedBy)))
                    .OrderByDescending(e => e.ReceivedAt),
            ];
            return new EncounterSourceResult(EncounterSourceStatus.Found, encounters);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, ex.GetType().Name);
            return EncounterSourceResult.Unavailable;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "API thông tin người bệnh trả mã {StatusCode} khi tra lượt tiếp nhận.")]
    private static partial void LogFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không gọi được API thông tin người bệnh để tra lượt tiếp nhận ({Error}).")]
    private static partial void LogUnreachable(ILogger logger, string error);

    private sealed record SearchRequest(string? PatientCode, string? CitizenId, DateOnly From, DateOnly To);

    private sealed record SearchItem(string? EncounterNo, DateTime? ReceivedAt, string? Place, string? ReceivedBy);

    private sealed record SearchResponse(IReadOnlyList<SearchItem>? Items);
}
