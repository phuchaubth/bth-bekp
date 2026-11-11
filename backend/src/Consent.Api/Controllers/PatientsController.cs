using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Patients;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Người bệnh (màn hình 4, 9, 10). Số CCCD không bao giờ đi trong URL (tránh lộ qua log/proxy).</summary>
[ApiController]
[Route("api/v1/patients")]
[Produces("application/json")]
public sealed class PatientsController(PatientService patients) : ControllerBase
{
    /// <summary>Tìm kiếm theo họ tên (không dấu), mã người bệnh hoặc số CCCD; số CCCD trong kết quả đã được che.</summary>
    [HttpGet]
    [HasPermission(Permissions.PatientsRead)]
    public async Task<PagedResult<PatientListItemDto>> Search([FromQuery] PatientQuery query, CancellationToken cancellationToken) =>
        await patients.SearchAsync(query, cancellationToken).ConfigureAwait(false);

    /// <summary>Hồ sơ đầy đủ (mỗi lần xem được ghi nhật ký).</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PatientsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.GetAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Tra cứu sau khi quét CCCD: hồ sơ đã có và thông tin trên HIS.</summary>
    [HttpPost("lookup")]
    [HasPermission(Permissions.PatientsRead)]
    public async Task<IActionResult> Lookup(PatientLookupRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.LookupAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [HasPermission(Permissions.PatientsManage)]
    public async Task<IActionResult> Create(PatientRequest request, CancellationToken cancellationToken)
    {
        var result = await patients.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PatientsManage)]
    public async Task<IActionResult> Update(Guid id, PatientRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/representatives")]
    [HasPermission(Permissions.PatientsManage)]
    public async Task<IActionResult> AddRepresentative(Guid id, RepresentativeRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.AddRepresentativeAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}/representatives/{representativeId:guid}")]
    [HasPermission(Permissions.PatientsManage)]
    public async Task<IActionResult> UpdateRepresentative(
        Guid id, Guid representativeId, RepresentativeRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.UpdateRepresentativeAsync(id, representativeId, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}/representatives/{representativeId:guid}")]
    [HasPermission(Permissions.PatientsManage)]
    public async Task<IActionResult> RemoveRepresentative(Guid id, Guid representativeId, CancellationToken cancellationToken) =>
        this.ToActionResult(await patients.RemoveRepresentativeAsync(id, representativeId, cancellationToken).ConfigureAwait(false));
}
