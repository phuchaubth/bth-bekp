using BEKP.Application.Dashboard;
using BEKP.Application.TimeStandards;
using BEKP.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BEKP.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = BekpPolicies.DashboardRead)]
public sealed class DashboardController(IDashboardQueryService queries, ITimeStandardsService timeStandards) : ControllerBase
{
    [HttpGet("departments")]
    public Task<IReadOnlyList<DepartmentListItemDto>> GetDepartments(CancellationToken cancellationToken) =>
        queries.GetDepartmentsAsync(cancellationToken);

    [HttpGet("dashboard/overview")]
    public Task<HospitalOverviewDto> GetOverview(CancellationToken cancellationToken) =>
        queries.GetOverviewAsync(cancellationToken);

    [HttpGet("dashboard/departments/{departmentCode:regex(^DEP-\\d{{4}}$)}")]
    public Task<DepartmentDetailDto> GetDepartment(string departmentCode, CancellationToken cancellationToken) =>
        queries.GetDepartmentAsync(departmentCode, cancellationToken);

    /// <summary>Compliance with regulated process times, hospital-wide or for one department.</summary>
    [HttpGet("dashboard/time-standards")]
    public async Task<ActionResult<TimeStandardsReportDto>> GetTimeStandards(
        [FromQuery] ReportPeriod period = ReportPeriod.Today,
        [FromQuery(Name = "department")] string? departmentCode = null,
        CancellationToken cancellationToken = default)
    {
        if (departmentCode is not null && !Domain.Common.BusinessCodes.IsDepartmentCode(departmentCode))
        {
            ModelState.AddModelError("department", "Must match DEP-0000.");
            return ValidationProblem(ModelState);
        }

        return await timeStandards.GetReportAsync(period, departmentCode, cancellationToken).ConfigureAwait(false);
    }
}
