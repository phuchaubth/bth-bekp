using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Shared;

namespace Consent.Application.QualityControl;

/// <summary>
/// Dashboard giám sát nội kiểm đường huyết tại giường (quyết định 59), chỉ đọc. Khoa HSH (<see cref="Permissions.QcReview"/>) và
/// người có quyền chỉ xem dashboard (<see cref="Permissions.QcMonitor"/>: lãnh đạo, Phòng Điều dưỡng, QLCL) thấy mọi khoa; khoa
/// (<see cref="Permissions.QcRecord"/>) thấy khoa của mình. Cách tính ở <see cref="QcDashboardBuilder"/>.
/// </summary>
public sealed class QcDashboardService(
    IQualityControlRepository qc,
    IDepartmentRepository departments,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>Xem được dashboard của tối đa bấy nhiêu tháng trước (đủ báo cáo năm trước).</summary>
    public const int MaxMonthsBack = 24;

    private static readonly Error Forbidden = Error.Forbidden(
        "qc.forbidden", "Dashboard nội kiểm dành cho khoa ghi kết quả nội kiểm, Khoa HSH và người được cấp quyền xem dashboard.");

    private bool CanReview => currentUser.HasPermission(Permissions.QcReview);

    private bool CanMonitor => currentUser.HasPermission(Permissions.QcMonitor);

    private bool CanRecordAny => currentUser.HasPermission(Permissions.QcRecord);

    /// <summary>Dashboard của một tháng (<c>yyyy-MM</c>, mặc định tháng này).</summary>
    public async Task<Result<QcDashboardDto>> GetAsync(string? month, CancellationToken cancellationToken)
    {
        if (!CanReview && !CanMonitor && !CanRecordAny)
        {
            return Forbidden;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);
        var current = new DateOnly(today.Year, today.Month, 1);
        if (QcBookService.ParseMonth(month ?? current.ToString("yyyy-MM", CultureInfo.InvariantCulture)) is not { } first)
        {
            return Error.ValidationField("month", "Tháng dạng yyyy-MM.");
        }

        if (first > current)
        {
            return Error.ValidationField("month", "Chưa tới tháng này.");
        }

        if (first < current.AddMonths(-MaxMonthsBack))
        {
            return Error.ValidationField("month", $"Chỉ xem được {MaxMonthsBack} tháng gần đây.");
        }

        // Từ tháng đầu của biểu đồ xu hướng đến tháng này (cảnh báo tính đến hôm nay: sổ tháng này và các tháng trước).
        var books = await qc.ListBooksBetweenAsync(first.AddMonths(1 - QcDashboardBuilder.TrendMonths), current, cancellationToken)
            .ConfigureAwait(false);
        var meters = await qc.ListMetersAsync(cancellationToken).ConfigureAwait(false);
        var lots = await qc.ListLotsAsync(cancellationToken).ConfigureAwait(false);
        var names = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.Id, d => d.Name);
        return QcDashboardBuilder.Build(first, today, meters, lots, books, names, CanSee, CanOpen);
    }

    private bool CanRecord(Guid departmentId) =>
        CanRecordAny
        && (currentUser.HasPermission(Permissions.ScopeAllDepartments) || currentUser.DepartmentIds.Contains(departmentId));

    /// <summary>Khoa có trong dashboard của người xem.</summary>
    private bool CanSee(Guid departmentId) => CanReview || CanMonitor || CanRecord(departmentId);

    /// <summary>Mở được sổ như <see cref="QcBookService"/>: khoa của sổ hoặc Khoa HSH (quyền chỉ xem dashboard thì không).</summary>
    private bool CanOpen(Guid departmentId) => CanReview || CanRecord(departmentId);
}
