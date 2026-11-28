using Consent.Application.Abstractions;
using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.CriticalResults;
using Consent.Shared;

namespace Consent.Application.CriticalResults;

/// <summary>
/// Danh mục kết quả nguy cấp (Phụ lục 01 QT-KQNC; quyết định 60): QLCL (<see cref="Permissions.CriticalManage"/>) thêm, sửa, ngưng
/// dùng; khoa cận lâm sàng chọn từ các mục đang dùng khi ghi. Không xóa (hồ sơ đã ghi chép lại tên và khoảng nguy cấp lúc ghi).
/// </summary>
public sealed class CriticalCatalogService(
    ICriticalResultRepository critical,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly Error Forbidden = Error.Forbidden(
        "critical.forbidden", "Chỉ người quản lý danh mục kết quả nguy cấp được thêm, sửa danh mục.");

    private static readonly Error NotFound = Error.NotFound("critical.test_not_found", "Không tìm thấy mục trong danh mục kết quả nguy cấp.");

    /// <summary>Danh mục: người quản lý thấy cả mục ngưng dùng; khoa cận lâm sàng thấy mục đang dùng để chọn khi ghi.</summary>
    public async Task<Result<IReadOnlyList<CriticalTestDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var manage = currentUser.HasPermission(Permissions.CriticalManage);
        if (!manage && !currentUser.HasPermission(Permissions.CriticalRecord) && !currentUser.HasPermission(Permissions.CriticalMonitor))
        {
            return Error.Forbidden("critical.forbidden", "Không có quyền xem danh mục kết quả nguy cấp.");
        }

        var tests = await critical.ListTestsAsync(cancellationToken).ConfigureAwait(false);
        return tests
            .Where(t => manage || t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name, StringComparer.CurrentCulture)
            .Select(CriticalTestDto.From)
            .ToList();
    }

    public async Task<Result<CriticalTestDto>> CreateAsync(SaveCriticalTestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!currentUser.HasPermission(Permissions.CriticalManage))
        {
            return Forbidden;
        }

        CriticalTest test;
        try
        {
            test = CriticalTest.Create(Details(request), clock.UtcNow);
            if (!request.IsActive)
            {
                test.Update(Details(request), isActive: false, clock.UtcNow);
            }
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("critical.rule", ex.Message);
        }

        critical.Add(test);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CriticalTestDto.From(test);
    }

    public async Task<Result<CriticalTestDto>> UpdateAsync(Guid id, SaveCriticalTestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!currentUser.HasPermission(Permissions.CriticalManage))
        {
            return Forbidden;
        }

        if (await critical.GetTestAsync(id, cancellationToken).ConfigureAwait(false) is not { } test)
        {
            return NotFound;
        }

        try
        {
            test.Update(Details(request), request.IsActive, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("critical.rule", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CriticalTestDto.From(test);
    }

    private static CriticalTestDetails Details(SaveCriticalTestRequest request) => new(
        request.Name, request.AdultRange, request.ChildRange, request.Unit, request.Section, request.Logbook, request.TextResult, request.SortOrder);
}
