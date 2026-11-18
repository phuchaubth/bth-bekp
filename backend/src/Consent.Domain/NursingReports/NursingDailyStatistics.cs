using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

public enum NursingStatisticsSource
{
    /// <summary>Lấy từ HIS qua API thông tin người bệnh.</summary>
    His,

    /// <summary>
    /// Phòng Điều dưỡng nhập tay (trước quyết định 35). Từ quyết định 35, khi HIS lỗi thì từng khoa nhập số của khoa
    /// (<see cref="NursingDepartmentStatistics"/>); số cũ loại này vẫn được đọc.
    /// </summary>
    Manual,
}

/// <summary>
/// Số liệu của một ngày trên bảng tổng hợp mà khoa không báo cáo (quyết định 31): tổng số người bệnh đến khám, số liệu chẩn
/// đoán hình ảnh, người bệnh phòng khám chờ nhập viện của từng khoa. Lấy từ HIS và lưu lại, để bảng tổng hợp của ngày cũ
/// không đổi theo HIS (số chờ nhập viện là số tại lúc lấy). Khi HIS lỗi, từng khoa nhập số thuộc nhiệm vụ của khoa
/// (<see cref="NursingDepartmentStatistics"/>, quyết định 35). Không có thông tin người bệnh.
/// </summary>
public sealed class NursingDailyStatistics : Entity, IAuditableEntity
{
    public const int ValuesMaxLength = 20_000;

    private NursingDailyStatistics(Guid id, DateOnly reportDate)
        : base(id)
    {
        ReportDate = reportDate;
        ValuesJson = "{}";
    }

    public DateOnly ReportDate { get; private set; }

    /// <summary>Số liệu dạng JSON; cấu trúc do tầng Application quy định.</summary>
    public string ValuesJson { get; private set; }

    public NursingStatisticsSource Source { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NursingDailyStatistics Create(
        DateOnly reportDate, string valuesJson, NursingStatisticsSource source, Guid userId, DateTimeOffset nowUtc)
    {
        var statistics = new NursingDailyStatistics(Guid.CreateVersion7(), reportDate);
        statistics.Set(valuesJson, source, userId, nowUtc);
        return statistics;
    }

    public void Set(string valuesJson, NursingStatisticsSource source, Guid userId, DateTimeOffset nowUtc)
    {
        var values = (valuesJson ?? string.Empty).Trim();
        if (values.Length == 0 || values.Length > ValuesMaxLength)
        {
            throw new DomainException("Số liệu tổng hợp không hợp lệ.");
        }

        ValuesJson = values;
        Source = source;
        UpdatedByUserId = userId;
        UpdatedAtUtc = nowUtc;
    }
}
