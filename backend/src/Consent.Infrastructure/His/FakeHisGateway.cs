using Consent.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.His;

/// <summary>
/// HIS giả lập, kết quả xác định theo số CCCD (để kiểm thử và demo, không có dữ liệu thật):
/// 3 số cuối "999" → HIS không khả dụng; số cuối "0" → không tìm thấy; còn lại → tìm thấy,
/// số cuối chẵn → đối tượng BHYT, lẻ → dịch vụ.
/// </summary>
internal sealed partial class FakeHisGateway(
    IOptions<HisGatewayOptions> options,
    IClock clock,
    ILogger<FakeHisGateway> logger) : IHisGateway
{
    public bool IsEnabled => true;

    public async Task<HisLookupResult> FindPatientByCitizenIdAsync(string citizenId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(citizenId);
        await Task.Delay(Math.Max(0, options.Value.FakeLatencyMilliseconds), cancellationToken).ConfigureAwait(false);

        if (citizenId.EndsWith("999", StringComparison.Ordinal))
        {
            LogUnavailable(logger);
            throw new HttpRequestException("HIS giả lập: mô phỏng mất kết nối.");
        }

        if (citizenId.EndsWith('0'))
        {
            return HisLookupResult.NotFound;
        }

        var suffix = citizenId[^8..];
        var insured = (citizenId[^1] - '0') % 2 == 0;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var encounter = new HisEncounter(
            $"TN{clock.UtcNow:yyyyMMdd}{citizenId[^4..]}",
            clock.UtcNow.AddMinutes(-30),
            "Khoa Khám bệnh",
            "Phòng khám Nội 1");

        return new HisLookupResult(
            HisLookupStatus.Found,
            new HisPatient(
                $"BN{suffix}",
                FullName: null,
                DateOfBirth: null,
                CoverageType: insured ? "BHYT" : "Dịch vụ",
                HealthInsuranceNumber: insured ? $"DN479{citizenId[^10..]}" : null,
                HealthInsuranceValidTo: insured ? today.AddYears(1) : null,
                CurrentEncounter: encounter));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "HIS giả lập: mô phỏng HIS không khả dụng.")]
    private static partial void LogUnavailable(ILogger logger);
}
