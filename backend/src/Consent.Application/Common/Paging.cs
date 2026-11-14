using Consent.Shared;

namespace Consent.Application.Common;

public static class Paging
{
    public const int DefaultPageSize = 20;

    /// <summary>Chuẩn hóa tham số phân trang: trang ≥ 1, kích thước trang trong khoảng 1..<see cref="PagedResult{T}.MaxPageSize"/>.</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, PagedResult<object>.MaxPageSize));
}
