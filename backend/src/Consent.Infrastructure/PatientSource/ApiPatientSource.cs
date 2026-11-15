using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Domain.Patients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.PatientSource;

/// <summary>Kết nối HTTP tới API thông tin người bệnh; tách riêng để kiểm thử thay được bộ xử lý HTTP.</summary>
public sealed class PatientSourceHttp(HttpMessageHandler handler) : IDisposable
{
    public HttpClient Client { get; } = new(handler, disposeHandler: true);

    public void Dispose() => Client.Dispose();
}

/// <summary>Nguồn chưa cấu hình (<c>PatientSource:Provider</c> = <c>None</c>).</summary>
internal sealed class DisabledPatientSource : IPatientSource
{
    public bool IsEnabled => false;

    public Task<PatientSourceResult> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken) =>
        Task.FromResult(PatientSourceResult.Disabled);
}

/// <summary>
/// Gọi <c>POST api/v1/patients/lookup</c> của API thông tin người bệnh (mã/CCCD đi trong thân, không nằm trên URL). Lỗi kết
/// nối, quá thời gian hay phản hồi lạ đều trả về <see cref="PatientSourceStatus.Unavailable"/> để người dùng nhập tay.
/// </summary>
internal sealed partial class ApiPatientSource(PatientSourceHttp http, IOptions<PatientSourceOptions> options, ILogger<ApiPatientSource> logger)
    : IPatientSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public async Task<PatientSourceResult> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl!), "api/v1/patients/lookup"))
        {
            Content = JsonContent.Create(new LookupRequest(patientCode, citizenId), options: Json),
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);

        try
        {
            using var response = await http.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return PatientSourceResult.NotFound;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return PatientSourceResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<LookupResponse>(Json, cancellationToken).ConfigureAwait(false);
            return body is null ? PatientSourceResult.Unavailable : new(PatientSourceStatus.Found, body.ToPatient());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, ex.GetType().Name);
            return PatientSourceResult.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API thông tin người bệnh trả mã {StatusCode}.")]
    private static partial void LogFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không gọi được API thông tin người bệnh ({Error}).")]
    private static partial void LogUnreachable(ILogger logger, string error);

    private sealed record LookupRequest(string? PatientCode, string? CitizenId);

    private sealed record LookupResponse(
        string? PatientCode,
        string? FullName,
        DateOnly? DateOfBirth,
        string? Gender,
        string? CitizenId,
        string? PhoneNumber,
        string? PermanentAddress,
        string? CurrentAddress,
        string? Ethnicity,
        string? Occupation,
        string? Nationality,
        string? HealthInsuranceNumber,
        DateOnly? CitizenIdIssueDate = null,
        string? OldIdNumber = null)
    {
        public ExternalPatient ToPatient() => new(
            PatientCode, FullName, DateOfBirth,
            Enum.TryParse<Gender>(Gender, ignoreCase: true, out var gender) && Enum.IsDefined(gender) ? gender : null,
            CitizenId, PhoneNumber, PermanentAddress, CurrentAddress, Ethnicity, Occupation, Nationality, HealthInsuranceNumber,
            CitizenIdIssueDate, OldIdNumber);
    }
}
