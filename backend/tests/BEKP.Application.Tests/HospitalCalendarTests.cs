using BEKP.Application.Common;
using Microsoft.Extensions.Options;

namespace BEKP.Application.Tests;

public sealed class HospitalCalendarTests
{
    [Fact]
    public void LocalDayStart_UsesHospitalTimeZone()
    {
        var calendar = new HospitalCalendar(Options.Create(new HospitalTimeOptions()));

        // 2026-11-17 18:30 UTC is already 2026-11-18 01:30 in Vietnam.
        var start = calendar.LocalDayStartUtc(new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 11, 17, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(new DateOnly(2026, 11, 18), calendar.LocalDate(new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc)));
    }
}
