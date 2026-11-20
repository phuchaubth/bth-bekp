using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consent.Application.NursingReports;

/// <summary>
/// Số liệu và phần tick của một báo cáo khoa, lưu dạng JSON
/// <c>{"values":{khóa:số},"checks":{khóa:"ok"|"issue"},"form":phiên bản mẫu}</c>. Ô để trống thì không có trong
/// <see cref="Values"/>. Khóa được sắp xếp khi ghi để nhật ký so sánh cũ/mới dễ đọc.
/// </summary>
public sealed record NursingReportFigures(IReadOnlyDictionary<string, int> Values, IReadOnlyDictionary<string, string> Checks)
{
    /// <summary>
    /// Phiên bản mẫu báo cáo khoa lúc lưu (<see cref="NursingReportForm.FormVersion"/>). Số liệu lưu trước khi có phiên bản là
    /// 1: các dòng thêm sau (<see cref="NursingReportItem.Since"/>) khoa chưa có để nhập, bảng tổng hợp để trống thay vì ghi 0.
    /// </summary>
    public int Version { get; init; } = NursingReportForm.FormVersion;

    public static NursingReportFigures Empty { get; } =
        new(new Dictionary<string, int>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));

    public int? Value(string key) => Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>"ok", "issue" hoặc <c>null</c> (chưa chọn).</summary>
    public string? Check(string key) => Checks.TryGetValue(key, out var value) ? value : null;

    public bool AllChecksAnswered => NursingReportForm.CheckKeys.All(Checks.ContainsKey);

    /// <summary>
    /// Báo cáo lưu trước khi tách "Số ca nhập viện ngày hôm trước" (quyết định 38): có số tổng cũ, không có hai dòng mới.
    /// </summary>
    public bool HasOnlyLegacyAdmissions =>
        Values.ContainsKey(NursingReportForm.LegacyAdmissionsKey) && !NursingReportForm.AdmissionKeys.Any(Values.ContainsKey);

    /// <summary>
    /// Đọc JSON đã lưu. Bỏ qua khóa không còn trong mẫu (mẫu bớt dòng) và giá trị sai kiểu, để báo cáo cũ luôn mở được. Riêng số
    /// tổng "Số ca nhập viện ngày hôm trước" cũ được giữ để nhận ra báo cáo cũ (<see cref="HasOnlyLegacyAdmissions"/>).
    /// </summary>
    public static NursingReportFigures Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return Empty;
        }

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root?["values"] is JsonObject valueNodes)
        {
            foreach (var (key, node) in valueNodes)
            {
                if ((NursingReportForm.ValueKeys.Contains(key) || key == NursingReportForm.LegacyAdmissionsKey)
                    && node is JsonValue value && value.TryGetValue<int>(out var number))
                {
                    values[key] = number;
                }
            }
        }

        var checks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root?["checks"] is JsonObject checkNodes)
        {
            foreach (var (key, node) in checkNodes)
            {
                if (NursingReportForm.CheckKeys.Contains(key) && node is JsonValue value && value.TryGetValue<string>(out var text)
                    && text is NursingReportForm.CheckOk or NursingReportForm.CheckIssue)
                {
                    checks[key] = text;
                }
            }
        }

        var version = root?["form"] is JsonValue form && form.TryGetValue<int>(out var formVersion) && formVersion > 0 ? formVersion : 1;
        return new NursingReportFigures(values, checks) { Version = version };
    }

    public string ToJson()
    {
        var values = new JsonObject();
        foreach (var (key, value) in Values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            values[key] = value;
        }

        var checks = new JsonObject();
        foreach (var (key, value) in Checks.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            checks[key] = value;
        }

        return new JsonObject { ["values"] = values, ["checks"] = checks, ["form"] = Version }.ToJsonString();
    }

    /// <summary>
    /// Tính lại các dòng tính tự động (<see cref="NursingReportForm.FormulaItems"/>, quyết định 46) theo công thức, bỏ số gửi lên
    /// cho các dòng đó; chưa tính được thì để trống.
    /// </summary>
    public NursingReportFigures WithFormulas()
    {
        var values = new Dictionary<string, int>(Values, StringComparer.Ordinal);
        foreach (var item in NursingReportForm.FormulaItems)
        {
            values.Remove(item.Key);
            if (item.Formula!.Evaluate(values) is { } result)
            {
                values[item.Key] = result;
            }
        }

        return new NursingReportFigures(values, Checks) { Version = Version };
    }

    /// <summary>Chỉ giữ các dòng điền sẵn cho báo cáo mới (nhân sự, chỉ tiêu) từ báo cáo trước.</summary>
    public NursingReportFigures CarryOver() => new(
        NursingReportForm.CarryOverKeys.Where(Values.ContainsKey).ToDictionary(k => k, k => Values[k], StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal));
}
