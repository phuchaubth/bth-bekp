using Consent.Application.Abstractions;

namespace Consent.Infrastructure.His;

/// <summary>Chưa kết nối HIS: không gọi ra ngoài, mọi tra cứu trả về trạng thái "Disabled".</summary>
internal sealed class DisabledHisGateway : IHisGateway
{
    public bool IsEnabled => false;

    public Task<HisLookupResult> FindPatientByCitizenIdAsync(string citizenId, CancellationToken cancellationToken) =>
        Task.FromResult(HisLookupResult.Disabled);
}
