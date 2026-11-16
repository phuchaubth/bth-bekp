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
    /// Tùy chọn (quyết định 25): câu SELECT trả về <b>các lượt tiếp nhận</b> của một người bệnh trong khoảng thời gian, nhận
    /// tham số <c>@PatientCode</c>, <c>@CitizenId</c> (tham số không dùng thì là NULL), <c>@From</c> (đầu ngày bắt đầu) và
    /// <c>@To</c> (đầu ngày <b>sau</b> ngày kết thúc: lọc <c>ThoiGian &gt;= @From AND ThoiGian &lt; @To</c>). Tên cột kết quả:
    /// EncounterNo (số tiếp nhận), ReceivedAt (thời gian tiếp nhận, giờ bệnh viện), Place (nơi tiếp nhận), ReceivedBy (người
    /// tiếp nhận). Để trống thì API không có chức năng tra lượt tiếp nhận.
    /// </summary>
    public string? EncounterQuery { get; set; }

    /// <summary>
    /// Tùy chọn (quyết định 31): câu SELECT trả về <b>số liệu trong ngày</b> cho bảng tổng hợp điều dưỡng, nhận tham số
    /// <c>@From</c> (đầu ngày) và <c>@To</c> (đầu ngày hôm sau: lọc <c>ThoiGian &gt;= @From AND ThoiGian &lt; @To</c>). Mỗi
    /// dòng một số liệu, tên cột: Indicator (mã chỉ số: outpatientVisits, waitingAdmission, ultrasound, echocardiography,
    /// ecg, xray, ct, mri), DepartmentCode (mã khoa trong phần mềm phiếu, chỉ cần với waitingAdmission), Value (số nguyên ≥ 0).
    /// Thường viết bằng nhiều câu SELECT nối bằng UNION ALL. Để trống thì API không có chức năng này.
    /// </summary>
    public string? DailyStatisticsQuery { get; set; }

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

    public bool HasEncounterQuery => !string.IsNullOrWhiteSpace(EncounterQuery);

    /// <summary>Câu truy vấn lượt tiếp nhận (nếu đặt): một câu SELECT, dùng @From, @To và @PatientCode/@CitizenId.</summary>
    public bool HasValidEncounterQuery =>
        !HasEncounterQuery
        || (IsSingleSelect(EncounterQuery, out var query)
            && query.Contains("@From", StringComparison.OrdinalIgnoreCase)
            && query.Contains("@To", StringComparison.OrdinalIgnoreCase)
            && (query.Contains("@PatientCode", StringComparison.OrdinalIgnoreCase) || query.Contains("@CitizenId", StringComparison.OrdinalIgnoreCase)));

    public bool HasDailyStatisticsQuery => !string.IsNullOrWhiteSpace(DailyStatisticsQuery);

    /// <summary>Câu truy vấn số liệu trong ngày (nếu đặt): một câu SELECT dùng @From và @To.</summary>
    public bool HasValidDailyStatisticsQuery =>
        !HasDailyStatisticsQuery
        || (IsSingleSelect(DailyStatisticsQuery, out var query)
            && query.Contains("@From", StringComparison.OrdinalIgnoreCase)
            && query.Contains("@To", StringComparison.OrdinalIgnoreCase));

    private static bool IsSingleSelect(string? value, out string query)
    {
        query = value?.Trim().TrimEnd(';').Trim() ?? string.Empty;
        return query.Length > 0 && !query.Contains(';', StringComparison.Ordinal)
            && (query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || query.StartsWith("WITH", StringComparison.OrdinalIgnoreCase));
    }

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
