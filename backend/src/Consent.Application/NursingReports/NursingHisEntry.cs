using Consent.Domain.NursingReports;

namespace Consent.Application.NursingReports;

/// <summary>
/// Số liệu HIS khoa nhập tay khi HIS lỗi (quyết định 35): khoa nào nhập dòng nào, và gộp với số lấy từ HIS cho bảng tổng hợp.
/// Khoa phải báo cáo nhập "NB PK chờ nhập viện" của khoa mình; mỗi chỉ số toàn viện (mục B, D) do khoa được Phòng Điều dưỡng
/// giao nhập. Phòng Điều dưỡng không nhập thay. Số lấy từ HIS luôn được dùng trước; ô HIS không có số thì dùng số khoa nhập.
/// </summary>
public static class NursingHisEntry
{
    /// <summary>Chỉ số toàn viện theo thứ tự trên bảng tổng hợp (mục B rồi mục D).</summary>
    public static IReadOnlyList<NursingSummaryLine> HospitalLines { get; } =
        [.. NursingReportForm.OutpatientLines, .. NursingReportForm.ImagingLines];

    /// <summary>Đọc cấu hình <c>chỉ số:khoa,…</c> (<see cref="Domain.Settings.SettingKeys.NursingHisEntryDepartments"/>); mục hỏng hoặc chỉ số lạ bị bỏ qua.</summary>
    public static IReadOnlyDictionary<string, Guid> ParseAssignments(string? value)
    {
        var assignments = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && NursingHisIndicators.HospitalWide.Contains(pair[0]) && Guid.TryParse(pair[1], out var departmentId))
            {
                assignments[pair[0]] = departmentId;
            }
        }

        return assignments;
    }

    /// <summary>Ghi cấu hình theo thứ tự trên bảng tổng hợp; không giao chỉ số nào thì trả <c>null</c>.</summary>
    public static string? FormatAssignments(IReadOnlyDictionary<string, Guid> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var parts = HospitalLines
            .Where(l => assignments.ContainsKey(l.Key))
            .Select(l => l.Key + ":" + assignments[l.Key].ToString("D"))
            .ToList();
        return parts.Count == 0 ? null : string.Join(',', parts);
    }

    /// <summary>Các dòng khoa nhập: "NB PK chờ nhập viện" (khoa phải báo cáo), rồi chỉ số toàn viện được giao cho khoa.</summary>
    public static IReadOnlyList<NursingSummaryLine> LinesFor(Guid departmentId, bool isReporting, IReadOnlyDictionary<string, Guid> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var lines = new List<NursingSummaryLine>();
        if (isReporting)
        {
            lines.Add(new NursingSummaryLine(NursingHisIndicators.WaitingAdmission, NursingReportForm.WaitingAdmissionLabel));
        }

        lines.AddRange(HospitalLines.Where(l => assignments.TryGetValue(l.Key, out var assigned) && assigned == departmentId));
        return lines;
    }

    /// <summary>Số khoa nhập, lưu cùng dạng với số liệu HIS: chỉ số toàn viện, và "NB PK chờ nhập viện" của chính khoa.</summary>
    public static NursingStatisticsValues ToValues(Guid departmentId, IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new NursingStatisticsValues(
            values.Where(p => NursingHisIndicators.HospitalWide.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            values.TryGetValue(NursingHisIndicators.WaitingAdmission, out var waiting)
                ? new Dictionary<Guid, int> { [departmentId] = waiting }
                : new Dictionary<Guid, int>());
    }

    /// <summary>Số của một dòng khoa nhập trong một bộ số liệu.</summary>
    public static int? Value(NursingStatisticsValues values, Guid departmentId, string key)
    {
        ArgumentNullException.ThrowIfNull(values);
        return key == NursingHisIndicators.WaitingAdmission ? values.Waiting(departmentId) : values.HospitalValue(key);
    }

    /// <summary>
    /// Gộp số lấy từ HIS với số các khoa nhập: số HIS dùng trước. Chỉ số toàn viện HIS không có thì lấy số khoa nhập gần nhất;
    /// "NB PK chờ nhập viện" HIS không có thì lấy số chính khoa đó nhập. Trả kèm khoa đã nhập từng số được dùng.
    /// </summary>
    public static NursingMergedStatistics Merge(NursingStatisticsValues his, IReadOnlyList<NursingDepartmentStatistics> entries)
    {
        ArgumentNullException.ThrowIfNull(his);
        ArgumentNullException.ThrowIfNull(entries);
        var hospital = new Dictionary<string, int>(his.Hospital, StringComparer.Ordinal);
        var waiting = new Dictionary<Guid, int>(his.WaitingAdmission);
        var hospitalEnteredBy = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var waitingEnteredBy = new HashSet<Guid>();
        foreach (var entry in entries.OrderByDescending(e => e.UpdatedAtUtc))
        {
            var values = NursingStatisticsValues.Parse(entry.ValuesJson);
            foreach (var (key, value) in values.Hospital)
            {
                if (hospital.TryAdd(key, value))
                {
                    hospitalEnteredBy[key] = entry.DepartmentId;
                }
            }

            if (values.Waiting(entry.DepartmentId) is { } own && waiting.TryAdd(entry.DepartmentId, own))
            {
                waitingEnteredBy.Add(entry.DepartmentId);
            }
        }

        return new NursingMergedStatistics(new NursingStatisticsValues(hospital, waiting), hospitalEnteredBy, waitingEnteredBy);
    }
}

/// <summary>
/// Số liệu dùng cho bảng tổng hợp sau khi gộp. <paramref name="HospitalEnteredBy"/>: chỉ số toàn viện lấy từ số khoa nhập,
/// kèm khoa đã nhập. <paramref name="WaitingEnteredBy"/>: khoa có "NB PK chờ nhập viện" lấy từ số khoa tự nhập.
/// </summary>
public sealed record NursingMergedStatistics(
    NursingStatisticsValues Values,
    IReadOnlyDictionary<string, Guid> HospitalEnteredBy,
    IReadOnlySet<Guid> WaitingEnteredBy)
{
    public bool UsesDepartmentEntries => HospitalEnteredBy.Count > 0 || WaitingEnteredBy.Count > 0;
}
