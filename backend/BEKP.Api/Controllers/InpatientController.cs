using BEKP.Api.Contracts;
using BEKP.Application.Inpatient;
using BEKP.Domain.Transfusion;
using BEKP.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BEKP.Api.Controllers;

/// <summary>Integration endpoints for HIS / LIS / blood bank systems. Thin: validation and business rules live in the application layer.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = BekpPolicies.InpatientWrite)]
public sealed class InpatientController(IInpatientCommandService commands) : ControllerBase
{
    private const string StayRoute = "{stayCode:regex(^ADM-\\d{{6}}$)}";

    [HttpPost("inpatient/stays")]
    public async Task<IActionResult> Admit(AdmitStayRequest request, CancellationToken cancellationToken)
    {
        await commands.AdmitAsync(new AdmitStayCommand(request.DepartmentCode, request.BedCode, request.StayCode, request.CareLevel!.Value, request.PrincipalDiagnosisCode, request.ExpectedDischargeDate, request.AdmittedAt), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("inpatient/stays/" + StayRoute + "/discharge")]
    public async Task<IActionResult> Discharge(string stayCode, DischargeStayRequest request, CancellationToken cancellationToken)
    {
        await commands.DischargeAsync(new DischargeStayCommand(stayCode, request.DischargedAt), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("inpatient/stays/" + StayRoute + "/clinical-status")]
    public async Task<IActionResult> UpdateClinicalStatus(string stayCode, UpdateClinicalStatusRequest request, CancellationToken cancellationToken)
    {
        await commands.UpdateClinicalStatusAsync(new UpdateClinicalStatusCommand(stayCode, request.CareLevel!.Value, request.PrincipalDiagnosisCode, request.IsNeutropenic, request.IsTransfusing, request.HasChemotherapyToday, request.ExpectedDischargeDate), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("inpatient/departments/{departmentCode:regex(^DEP-\\d{{4}}$)}/beds/{bedCode}/status")]
    public async Task<IActionResult> UpdateBedStatus(string departmentCode, string bedCode, UpdateBedStatusRequest request, CancellationToken cancellationToken)
    {
        await commands.UpdateBedStatusAsync(new UpdateBedStatusCommand(departmentCode, bedCode, request.Status!.Value), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("inpatient/stays/" + StayRoute + "/febrile-neutropenia")]
    public async Task<ActionResult<CreatedResponse>> RecordFever(string stayCode, RecordFeverRequest request, CancellationToken cancellationToken)
    {
        var id = await commands.RecordFeverAsync(new RecordFeverCommand(stayCode, request.TemperatureCelsius, request.DetectedAt), cancellationToken).ConfigureAwait(false);
        return StatusCode(StatusCodes.Status201Created, new CreatedResponse(id));
    }

    [HttpPost("inpatient/febrile-neutropenia/{episodeId:guid}/antibiotic")]
    public async Task<IActionResult> RecordAntibiotic(Guid episodeId, RecordAntibioticStartRequest request, CancellationToken cancellationToken)
    {
        await commands.RecordAntibioticStartAsync(new RecordAntibioticStartCommand(episodeId, request.StartedAt), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("transfusion/requests")]
    public async Task<ActionResult<CreatedResponse>> CreateBloodRequest(CreateBloodRequestRequest request, CancellationToken cancellationToken)
    {
        var modifiers = request.Modifiers.Aggregate(BloodProductModifiers.None, (acc, m) => acc | m);
        var id = await commands.CreateBloodRequestAsync(new CreateBloodRequestCommand(request.StayCode, request.Product!.Value, request.AboGroup!.Value, request.RhDPositive, request.Units, request.Urgency!.Value, modifiers), cancellationToken).ConfigureAwait(false);
        return StatusCode(StatusCodes.Status201Created, new CreatedResponse(id));
    }

    [HttpPut("transfusion/requests/{requestId:guid}/status")]
    public async Task<IActionResult> ChangeBloodRequestStatus(Guid requestId, ChangeBloodRequestStatusRequest request, CancellationToken cancellationToken)
    {
        await commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(requestId, request.Status!.Value), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("transfusion/inventory/{product}/{aboGroup}")]
    public async Task<IActionResult> SetInventory(BloodProductType product, AboGroup aboGroup, SetBloodInventoryRequest request, CancellationToken cancellationToken)
    {
        await commands.SetBloodInventoryAsync(new SetBloodInventoryCommand(product, aboGroup, request.UnitsAvailable, request.MinimumUnits), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
