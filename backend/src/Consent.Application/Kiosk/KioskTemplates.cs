using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Settings;

namespace Consent.Application.Kiosk;

/// <summary>
/// Danh sách mẫu người bệnh tự làm trên kiosk (quyết định 24), lưu trong cấu hình hệ thống dạng mã mẫu theo thứ tự làm.
/// Chỉ mẫu đang dùng, đã phát hành và người bệnh tự trả lời được mới dùng được trên kiosk.
/// </summary>
internal static class KioskTemplates
{
    /// <summary>Số mẫu tối đa trên kiosk (bằng số mẫu tối đa của một bộ phiếu).</summary>
    public const int MaxTemplates = ConsentFormService.MaxBundleSize;

    /// <summary>
    /// Loại trường người bệnh không tự nhập được trên kiosk: bảng nhiều dòng và ngày giờ (thường là phần chuyên môn do nhân
    /// viên điền).
    /// </summary>
    private static readonly HashSet<string> UnsupportedTypes = new([BlockTypes.Repeater, BlockTypes.DateTime], StringComparer.Ordinal);

    public static IReadOnlyList<string> ParseCodes(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : [.. stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ConsentTemplate.NormalizeCode)
                .Distinct(StringComparer.Ordinal)];

    public static string FormatCodes(IEnumerable<string> codes) => string.Join(',', codes);

    /// <summary>Mã mẫu đang cấu hình cho kiosk (theo thứ tự).</summary>
    public static async Task<IReadOnlyList<string>> ReadCodesAsync(ISystemSettingRepository settings, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return ParseCodes(stored.FirstOrDefault(s => s.Key == SettingKeys.KioskTemplateCodes)?.Value);
    }

    /// <summary>Lý do mẫu chưa dùng được trên kiosk; rỗng nếu dùng được.</summary>
    public static IReadOnlyList<string> Problems(ConsentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var problems = new List<string>();
        if (!template.IsActive)
        {
            problems.Add("Mẫu đang ngừng sử dụng.");
        }

        if (template.PublishedVersion is not { } version)
        {
            problems.Add("Mẫu chưa có phiên bản phát hành.");
            return problems;
        }

        var unsupported = ConsentTemplateService.ReadDefinition(version).InputBlocks
            .Where(b => UnsupportedTypes.Contains(b.Type))
            .Select(b => $"“{b.Label ?? b.Key}”")
            .ToList();
        if (unsupported.Count > 0)
        {
            problems.Add($"Có trường người bệnh không tự nhập được trên kiosk (bảng nhiều dòng hoặc ngày giờ): {string.Join(", ", unsupported)}.");
        }

        return problems;
    }

    /// <summary>
    /// Một lời xác nhận cho cả bộ: nhãn của mẫu đầu có ô lời xác nhận, bắt buộc nếu có mẫu bắt buộc, gộp câu gợi ý các mẫu
    /// (không trùng, tối đa 8) — giống màn hình ký cả bộ. Không mẫu nào có ô này thì không có.
    /// </summary>
    public static TemplateStatement? MergeStatement(IEnumerable<TemplateDefinition> definitions)
    {
        var configs = definitions.Select(d => d.Signatures?.Statement).OfType<TemplateStatement>().ToList();
        if (configs.Count == 0)
        {
            return null;
        }

        var suggestions = configs.SelectMany(c => c.Suggestions ?? []).Distinct(StringComparer.Ordinal)
            .Take(TemplateStatement.MaxSuggestions).ToList();
        return new TemplateStatement(configs[0].Label, configs.Exists(c => c.Required), suggestions);
    }
}
