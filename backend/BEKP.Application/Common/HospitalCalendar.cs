using Microsoft.Extensions.Options;

namespace BEKP.Application.Common;

public sealed class HospitalTimeOptions
{
    public const string SectionName = "Hospital";

    /// <summary>IANA time zone of the hospital, used to define "today".</summary>
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
}

/// <summary>Converts the hospital's local calendar day to UTC boundaries. All stored timestamps are UTC.</summary>
public sealed class HospitalCalendar
{
    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(7);
    private readonly TimeZoneInfo? timeZone;

    public HospitalCalendar(IOptions<HospitalTimeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(options.Value.TimeZoneId, out var zone) ? zone : null;
    }

    public DateTime LocalDayStartUtc(DateTime utcNow)
    {
        var offset = timeZone?.GetUtcOffset(utcNow) ?? FallbackOffset;
        var local = utcNow + offset;
        return DateTime.SpecifyKind(local.Date - offset, DateTimeKind.Utc);
    }

    public DateOnly LocalDate(DateTime utcNow)
    {
        var offset = timeZone?.GetUtcOffset(utcNow) ?? FallbackOffset;
        return DateOnly.FromDateTime(utcNow + offset);
    }
}
