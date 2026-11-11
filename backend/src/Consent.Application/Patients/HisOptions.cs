namespace Consent.Application.Patients;

/// <summary>Cấu hình kết nối HIS (mục <c>His</c>).</summary>
public sealed class HisOptions
{
    public const string SectionName = "His";

    /// <summary>Thời gian chờ tối đa khi gọi HIS; quá thời gian thì coi như HIS không khả dụng và cho phép nhập tay.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
