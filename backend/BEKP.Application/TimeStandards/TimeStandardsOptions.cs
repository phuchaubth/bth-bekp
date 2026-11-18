using BEKP.Domain.Timing;
using Microsoft.Extensions.Options;

namespace BEKP.Application.TimeStandards;

public sealed class TimeStandardSetting
{
    public int TargetMinutes { get; set; }

    /// <summary>Legal or internal reference (e.g. a Ministry of Health circular). Must be confirmed by the hospital.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>False until the quality department has confirmed target and reference.</summary>
    public bool ReferenceConfirmed { get; set; }
}

/// <summary>Configurable time standards (section "TimeStandards"). Missing entries fall back to provisional defaults.</summary>
public sealed class TimeStandardsOptions
{
    public const string SectionName = "TimeStandards";

    public Dictionary<TimeStandardType, TimeStandardSetting> Standards { get; set; } = [];
}

public sealed record TimeStandardDefinition(TimeStandardType Type, int TargetMinutes, string Reference, bool ReferenceConfirmed);

/// <summary>Resolves the effective target for each regulated process.</summary>
public sealed class TimeStandardCatalog
{
    private const string ProvisionalReference = "Mục tiêu tạm – cần Phòng QLCL xác nhận văn bản BYT/quy định nội bộ";

    private static readonly Dictionary<TimeStandardType, int> ProvisionalTargets = new()
    {
        [TimeStandardType.BloodIssueEmergency] = 30,
        [TimeStandardType.BloodIssueUrgent] = 60,
        [TimeStandardType.BloodIssueRoutine] = 240,
        [TimeStandardType.TransfusionStartAfterIssue] = 30,
        [TimeStandardType.FebrileNeutropeniaAntibiotic] = 60,
        [TimeStandardType.BedTurnaround] = 60,
    };

    private readonly IReadOnlyDictionary<TimeStandardType, TimeStandardDefinition> definitions;

    public TimeStandardCatalog(IOptions<TimeStandardsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.Standards;
        var result = new Dictionary<TimeStandardType, TimeStandardDefinition>();
        foreach (var type in Enum.GetValues<TimeStandardType>())
        {
            if (configured.TryGetValue(type, out var setting) && setting.TargetMinutes > 0)
            {
                var reference = string.IsNullOrWhiteSpace(setting.Reference) ? ProvisionalReference : setting.Reference.Trim();
                result[type] = new TimeStandardDefinition(type, setting.TargetMinutes, reference, setting.ReferenceConfirmed);
            }
            else
            {
                result[type] = new TimeStandardDefinition(type, ProvisionalTargets[type], ProvisionalReference, false);
            }
        }

        definitions = result;
    }

    public IReadOnlyCollection<TimeStandardDefinition> All => definitions.Values.OrderBy(d => d.Type).ToList();

    public TimeStandardDefinition Get(TimeStandardType type) => definitions[type];
}
