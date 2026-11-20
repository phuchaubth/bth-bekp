using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Shared;

namespace Consent.Application.NursingReports;

/// <summary>
/// Dashboard điều dưỡng của Phòng Điều dưỡng (quyết định 47): số liệu các khoa đã gửi báo cáo trong một khoảng ngày, theo ngày,
/// theo khoa và theo phần tick. Chỉ đọc. Quyền được kiểm tra ở API (<c>nursingreports.read</c>).
/// </summary>
public sealed class NursingDashboardService(INursingReportRepository reports, IDepartmentRepository departments, IClock clock)
{
    /// <summary>Số ngày xem được tối đa một lần (một quý).</summary>
    public const int MaxDays = 92;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>
    /// Dashboard <paramref name="days"/> ngày, đến hết ngày <paramref name="to"/> (bỏ trống là hôm nay, giờ Việt Nam). Ngày sau
    /// hôm nay chưa có báo cáo nên không nhận.
    /// </summary>
    public async Task<Result<NursingDashboardDto>> GetAsync(DateOnly? to, int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxDays)
        {
            return Error.ValidationField("days", $"Chọn từ 1 đến {MaxDays} ngày.");
        }

        var today = Today;
        var end = to ?? today;
        if (end > today)
        {
            return Error.ValidationField("to", "Không xem được ngày sau hôm nay.");
        }

        if (end.DayNumber < days)
        {
            return Error.ValidationField("to", "Ngày không hợp lệ.");
        }

        var from = end.AddDays(1 - days);
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.DepartmentId);
        var all = await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        var submitted = await reports.ListSubmittedFiguresAsync(from, end, cancellationToken).ConfigureAwait(false);
        return NursingDashboardBuilder.Build(from, end, NursingSummaryDepartment.From(all, configured), submitted);
    }
}
