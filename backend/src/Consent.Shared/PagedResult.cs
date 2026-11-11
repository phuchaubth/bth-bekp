namespace Consent.Shared;

/// <summary>Kết quả phân trang chuẩn cho mọi API danh sách: { items, page, pageSize, totalCount }.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public const int MaxPageSize = 100;

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
