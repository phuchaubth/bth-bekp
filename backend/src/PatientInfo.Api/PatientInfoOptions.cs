namespace PatientInfo.Api;

/// <summary>Cấu hình của API thông tin người bệnh (mục <c>PatientInfo</c>). Bí mật chỉ đặt bằng biến môi trường.</summary>
public sealed class PatientInfoOptions
{
    public const string SectionName = "PatientInfo";

    /// <summary>Độ dài tối thiểu của khóa API (ký tự), để khóa không đoán được.</summary>
    public const int MinimumApiKeyLength = 32;

    /// <summary>
    /// Chuỗi kết nối tới CSDL bệnh viện: biến môi trường <c>PatientInfo__ConnectionString</c>. Dùng tài khoản SQL chỉ có quyền
    /// đọc (SELECT) các bảng cần thiết.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Câu SELECT trả về <b>một dòng</b> người bệnh, nhận tham số <c>@PatientCode</c> và <c>@CitizenId</c> (tham số không
    /// dùng thì là NULL). Tên cột kết quả đặt bằng <c>AS</c>: PatientCode, FullName, DateOfBirth, Gender, CitizenId,
    /// CitizenIdIssueDate (ngày cấp CCCD), OldIdNumber (số CMND cũ), PhoneNumber, PermanentAddress, CurrentAddress, Ethnicity,
    /// Occupation, Nationality, HealthInsuranceNumber (thiếu cột nào thì trường đó để trống).
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Các khóa API được phép gọi (mỗi phần mềm một khóa, đổi khóa không gián đoạn bằng cách thêm khóa mới rồi bỏ khóa cũ):
    /// biến môi trường <c>PatientInfo__ApiKeys__0</c>, <c>PatientInfo__ApiKeys__1</c>…
    /// </summary>
    public string[] ApiKeys { get; set; } = [];

    /// <summary>Thời gian chờ tối đa của câu truy vấn (giây).</summary>
    public int QueryTimeoutSeconds { get; set; } = 10;

    private static readonly string[] DefaultMaleValues = ["Nam", "M", "Male", "1"];

    private static readonly string[] DefaultFemaleValues = ["Nữ", "Nu", "F", "Female", "0", "2"];

    /// <summary>
    /// Giá trị cột Gender được hiểu là Nam / Nữ (không phân biệt hoa thường). Để trống thì dùng mặc định (Nam/M/Male/1 và
    /// Nữ/Nu/F/Female/0/2); đặt cấu hình thì danh sách đặt <b>thay hẳn</b> mặc định (bộ đọc cấu hình .NET cộng thêm vào
    /// mảng có sẵn, nên mặc định không đặt ở đây).
    /// </summary>
    public string[] MaleValues { get; set; } = [];

    public string[] FemaleValues { get; set; } = [];

    public IReadOnlyList<string> EffectiveMaleValues => MaleValues.Length > 0 ? MaleValues : DefaultMaleValues;

    public IReadOnlyList<string> EffectiveFemaleValues => FemaleValues.Length > 0 ? FemaleValues : DefaultFemaleValues;

    /// <summary>Không giá trị nào vừa là Nam vừa là Nữ (ví dụ chỉ đặt MaleValues = 0 mà quên đặt FemaleValues).</summary>
    public bool HasUnambiguousGenderValues =>
        !EffectiveMaleValues.Intersect(EffectiveFemaleValues, StringComparer.OrdinalIgnoreCase).Any();

    public bool HasValidApiKeys => ApiKeys.Length > 0 && ApiKeys.All(k => k is { Length: >= MinimumApiKeyLength });

    /// <summary>
    /// Chỉ chấp nhận một câu SELECT (hoặc WITH … SELECT) có dùng tham số, không có dấu chấm phẩy giữa câu. Đây là lớp kiểm
    /// tra cấu hình; quyền chỉ đọc của tài khoản SQL mới là lớp bảo vệ chính.
    /// </summary>
    public bool HasValidQuery
    {
        get
        {
            var query = Query?.Trim().TrimEnd(';').Trim();
            if (string.IsNullOrEmpty(query) || query.Contains(';', StringComparison.Ordinal))
            {
                return false;
            }

            var startsWithSelect = query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || query.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
            var usesParameter = query.Contains("@PatientCode", StringComparison.OrdinalIgnoreCase)
                || query.Contains("@CitizenId", StringComparison.OrdinalIgnoreCase);
            return startsWithSelect && usesParameter;
        }
    }
}
