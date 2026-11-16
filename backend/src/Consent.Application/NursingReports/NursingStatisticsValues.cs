using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consent.Application.NursingReports;

/// <summary>
/// Số liệu một ngày của bảng tổng hợp không do khoa báo cáo (mục B, D và cột "NB PK chờ nhập viện"), lưu dạng JSON
/// <c>{"hospital":{chỉ số:số},"waitingAdmission":{mã khoa (Guid):số}}</c>. Chỉ số để trống thì không có trong JSON.
/// </summary>
public sealed record NursingStatisticsValues(IReadOnlyDictionary<string, int> Hospital, IReadOnlyDictionary<Guid, int> WaitingAdmission)
{
    public static NursingStatisticsValues Empty { get; } =
        new(new Dictionary<string, int>(StringComparer.Ordinal), new Dictionary<Guid, int>());

    public int? HospitalValue(string indicator) => Hospital.TryGetValue(indicator, out var value) ? value : null;

    public int? Waiting(Guid departmentId) => WaitingAdmission.TryGetValue(departmentId, out var value) ? value : null;

    public static NursingStatisticsValues Parse(string? json)
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

        var hospital = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root?["hospital"] is JsonObject hospitalNodes)
        {
            foreach (var (key, node) in hospitalNodes)
            {
                if (NursingHisIndicators.HospitalWide.Contains(key) && node is JsonValue value && value.TryGetValue<int>(out var number))
                {
                    hospital[key] = number;
                }
            }
        }

        var waiting = new Dictionary<Guid, int>();
        if (root?["waitingAdmission"] is JsonObject waitingNodes)
        {
            foreach (var (key, node) in waitingNodes)
            {
                if (Guid.TryParse(key, out var departmentId) && node is JsonValue value && value.TryGetValue<int>(out var number))
                {
                    waiting[departmentId] = number;
                }
            }
        }

        return new NursingStatisticsValues(hospital, waiting);
    }

    public string ToJson()
    {
        var hospital = new JsonObject();
        foreach (var (key, value) in Hospital.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hospital[key] = value;
        }

        var waiting = new JsonObject();
        foreach (var (key, value) in WaitingAdmission.OrderBy(p => p.Key))
        {
            waiting[key.ToString()] = value;
        }

        return new JsonObject { ["hospital"] = hospital, ["waitingAdmission"] = waiting }.ToJsonString();
    }
}
