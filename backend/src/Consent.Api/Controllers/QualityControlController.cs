using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.QualityControl;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Sổ theo dõi kết quả nội kiểm đường huyết tại giường (quyết định 57): Khoa HSH khai báo danh mục (máy đo, lô); khoa lâm sàng
/// ghi kết quả từng ngày, gửi sổ tháng; Khoa HSH xem xét từng ngày, xác nhận sổ tháng. Quyền xem, ghi theo khoa do nghiệp vụ kiểm
/// tra (khoa của sổ hoặc Khoa HSH).
/// </summary>
[ApiController]
[Route("api/v1/qc")]
[Produces("application/json")]
public sealed class QualityControlController(QcCatalogService catalog, QcBookService books) : ControllerBase
{
    /// <summary>Danh mục: máy đo và lô (kể cả ngưng sử dụng).</summary>
    [HttpGet("catalog")]
    [HasPermission(Permissions.QcManage)]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.GetAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost("meters")]
    [HasPermission(Permissions.QcManage)]
    public async Task<IActionResult> CreateMeter(SaveQcMeterRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.CreateMeterAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPut("meters/{id:guid}")]
    [HasPermission(Permissions.QcManage)]
    public async Task<IActionResult> UpdateMeter(Guid id, SaveQcMeterRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.UpdateMeterAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("lots")]
    [HasPermission(Permissions.QcManage)]
    public async Task<IActionResult> CreateLot(SaveQcLotRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.CreateLotAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPut("lots/{id:guid}")]
    [HasPermission(Permissions.QcManage)]
    public async Task<IActionResult> UpdateLot(Guid id, SaveQcLotRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.UpdateLotAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Sổ của một tháng (<c>yyyy-MM</c>, mặc định tháng này), lọc theo khoa; kèm máy chưa mở sổ.</summary>
    [HttpGet("books")]
    public async Task<IActionResult> ListBooks([FromQuery] string? month, [FromQuery] Guid? departmentId, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.ListAsync(month, departmentId, cancellationToken).ConfigureAwait(false));

    /// <summary>Mở sổ tháng của một máy (đã có thì trả lại sổ đó).</summary>
    [HttpPost("books")]
    public async Task<IActionResult> OpenBook(OpenQcBookRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.OpenAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpGet("books/{id:guid}")]
    public async Task<IActionResult> GetBook(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.GetAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa ghi kết quả của một ngày.</summary>
    [HttpPost("books/{id:guid}/runs")]
    public async Task<IActionResult> RecordRun(Guid id, QcRunRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.RecordRunAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa ghi nguyên nhân không đạt và kết quả chạy lại.</summary>
    [HttpPost("books/{id:guid}/runs/{runId:guid}/follow-up")]
    public async Task<IActionResult> RecordFollowUp(Guid id, Guid runId, QcFollowUpRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.RecordFollowUpAsync(id, runId, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa ghi điều chỉnh: hủy kết quả ghi sai (bắt buộc lý do), ghi kết quả thay thế nếu có.</summary>
    [HttpPost("books/{id:guid}/runs/{runId:guid}/correction")]
    public async Task<IActionResult> CorrectRun(Guid id, Guid runId, QcCorrectionRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.CorrectRunAsync(id, runId, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa HSH xem xét một hoặc nhiều ngày.</summary>
    [HttpPost("books/{id:guid}/reviews")]
    [HasPermission(Permissions.QcReview)]
    public async Task<IActionResult> ReviewDays(Guid id, QcReviewDaysRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.ReviewDaysAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa HSH trả lại một ngày đã xem xét (bắt buộc lý do).</summary>
    [HttpPost("books/{id:guid}/reviews/return")]
    [HasPermission(Permissions.QcReview)]
    public async Task<IActionResult> ReturnDay(Guid id, QcReturnDayRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.ReturnDayAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa gửi sổ tháng cho Khoa HSH.</summary>
    [HttpPost("books/{id:guid}/submit")]
    [HasPermission(Permissions.QcRecord)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.SubmitAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa HSH xác nhận sổ tháng (sổ khóa hẳn).</summary>
    [HttpPost("books/{id:guid}/confirm")]
    [HasPermission(Permissions.QcReview)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.ConfirmAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Khoa HSH trả lại sổ đã gửi (bắt buộc lý do).</summary>
    [HttpPost("books/{id:guid}/return")]
    [HasPermission(Permissions.QcReview)]
    public async Task<IActionResult> Return(Guid id, QcReasonRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await books.ReturnAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF sổ theo bố cục sổ giấy.</summary>
    [HttpGet("books/{id:guid}/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await books.RenderPdfAsync(id, cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(result, file => File(file.Content, "application/pdf", file.FileName));
    }
}
