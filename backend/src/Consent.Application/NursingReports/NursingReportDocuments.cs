namespace Consent.Application.NursingReports;

/// <summary>Dữ liệu dựng PDF báo cáo của một khoa. <paramref name="StatusLine"/>: dòng nhỏ cuối trang (đã gửi lúc…, bản nháp…).</summary>
public sealed record NursingReportPdfDocument(
    string HospitalName,
    string DepartmentName,
    DateOnly ReportDate,
    NursingReportFigures Figures,
    string? Notes,
    string StatusLine);

/// <summary>Dữ liệu dựng PDF và Excel bảng tổng hợp. <paramref name="PrintedLine"/>: dòng nhỏ cuối trang (in lúc…, số khoa đã gửi).</summary>
public sealed record NursingSummaryDocument(string HospitalName, NursingSummaryDto Summary, string PrintedLine);

/// <summary>In PDF báo cáo khoa và bảng tổng hợp theo bố cục bản giấy (quyết định 31).</summary>
public interface INursingReportPdfRenderer
{
    byte[] RenderReport(NursingReportPdfDocument document);

    byte[] RenderSummary(NursingSummaryDocument document);
}

/// <summary>Xuất bảng tổng hợp ra file Excel (.xlsx) để Phòng Điều dưỡng chỉnh sửa hoặc lưu riêng.</summary>
public interface INursingSummaryWorkbookWriter
{
    byte[] Write(NursingSummaryDocument document);
}
