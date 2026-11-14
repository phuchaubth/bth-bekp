using Consent.Application.ConsentForms;
using Consent.Application.Reports;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ReportServiceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    /// <summary>Tiếp đón Khoa Khám bệnh lập một phiếu CK-01; người ký đồng ý/từ chối; xác nhận sau <paramref name="minutes"/> phút.</summary>
    private static async Task<Guid> CompletedCk01(ConsentScenario s, int minutes, ConsentDecision decision, ConsentSignerKind signer)
    {
        var ct = CancellationToken.None;
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var id = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct)).Value.Id;
        var representative = signer == ConsentSignerKind.Representative ? s.Patient.Representatives.Single().Id : (Guid?)null;
        await s.Service.UpdateAsync(id, new(ConsentScenario.CompleteCk01(), signer, representative), ct);
        Assert.True((await s.Service.SubmitAsync(id, ct)).IsSuccess);
        Assert.True((await s.Signing.StartSigningAsync(id, ct)).IsSuccess);
        var back = s.UseSigningToken();
        Assert.True((await s.Signing.SignAsPatientAsync(id, new(decision, SignaturePng), ct)).IsSuccess);
        back();
        s.Ctx.Clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.True((await s.Signing.CompleteAsync(id, new(SignaturePng), ct)).IsSuccess);
        return id;
    }

    /// <summary>
    /// Lập ngày 11/11/2026 (giờ Việt Nam). Khoa Khám bệnh: 4 phiếu CK-01 hoàn tất sau 10, 30, 120 phút và 2 ngày (1 phiếu
    /// từ chối, 1 phiếu người đại diện ký), 1 phiếu nháp. Khoa Nội: 1 phiếu TN-01 nháp, 1 phiếu TN-01 đã hủy.
    /// </summary>
    private static async Task<ConsentScenario> Scenario()
    {
        var ct = CancellationToken.None;
        var s = ConsentScenario.Create();
        await CompletedCk01(s, 10, ConsentDecision.Agreed, ConsentSignerKind.Patient);
        await CompletedCk01(s, 30, ConsentDecision.Refused, ConsentSignerKind.Patient);
        await CompletedCk01(s, 120, ConsentDecision.Agreed, ConsentSignerKind.Representative);
        // Phiếu thứ tư lập ngày 11/11 (giờ Việt Nam), hoàn tất 2 ngày sau.
        s.Ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        await CompletedCk01(s, 2 * 24 * 60, ConsentDecision.Agreed, ConsentSignerKind.Patient);
        s.Ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 11, 11, 0, 0, TimeSpan.Zero);
        await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct);

        s.LogIn(s.Noi);
        await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), ct);
        var cancelled = (await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), ct)).Value.Id;
        Assert.True((await s.Service.CancelAsync(cancelled, new("Lập nhầm (thử nghiệm)"), ct)).IsSuccess);
        // Bây giờ: 19:00 ngày 13/11/2026 (giờ Việt Nam).
        s.Ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        return s;
    }

    private static void LogInAsReporter(ConsentScenario s) =>
        s.LogInAs(s.Kkb, SystemRoles.Reporter, Permissions.ReportsRead, Permissions.ScopeAllDepartments);

    [Fact]
    public async Task Summary_CountsByStatusDepartmentAndTemplate_WithRefusalRateSignersAndCompletionTime()
    {
        var s = await Scenario();
        LogInAsReporter(s);

        var result = await s.Ctx.CreateReportService().SummaryAsync(new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 13)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var report = result.Value;
        Assert.Equal(new StatusCounts(Total: 7, Draft: 2, PendingSignature: 0, Completed: 4, Cancelled: 1, Revoked: 0), report.Totals);
        Assert.Equal(new DecisionCounts(Agreed: 3, Refused: 1, RefusalRate: 0.25), report.Decisions);
        Assert.Equal(new SignerCounts(Patient: 3, Representative: 1), report.Signers);
        Assert.Equal(["KKB", "NOI"], report.ByDepartment.Select(d => d.Code));
        Assert.Equal(5, report.ByDepartment[0].Counts.Total);
        Assert.Equal(0.25, report.ByDepartment[0].Decisions.RefusalRate);
        Assert.Equal(new StatusCounts(2, 1, 0, 0, 1, 0), report.ByDepartment[1].Counts);
        Assert.Null(report.ByDepartment[1].Decisions.RefusalRate);
        Assert.Equal(["CK-01", "TN-01"], report.ByTemplate.Select(t => t.Code));

        var time = report.CompletionTime;
        Assert.Equal(4, time.Count);
        Assert.Equal((10 + 30 + 120 + 2880) / 4d, time.AverageMinutes);
        Assert.Equal(75, time.MedianMinutes);
        Assert.Equal(new[] { 1, 1, 1, 1 }, new[] { time.UpTo15Minutes, time.UpTo1Hour, time.UpTo1Day, time.Over1Day });
        Assert.InRange(time.Percentile90Minutes!.Value, 120, 2880);
    }

    [Fact]
    public async Task Summary_DailySeries_CoversEveryDayOfTheRange_InVietnamTime()
    {
        var s = await Scenario();
        // 16:59 UTC = 23:59 giờ Việt Nam ngày 12/11; 17:00 UTC = 00:00 ngày 13/11.
        s.LogIn(s.Noi);
        s.Ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 12, 16, 59, 0, TimeSpan.Zero);
        await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), CancellationToken.None);
        s.Ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 12, 17, 0, 0, TimeSpan.Zero);
        await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), CancellationToken.None);
        LogInAsReporter(s);

        var report = (await s.Ctx.CreateReportService().SummaryAsync(new(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13)), CancellationToken.None)).Value;

        Assert.Equal(
            [new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13)],
            report.Daily.Select(d => d.Date));
        Assert.Equal([0, 7, 1, 1], report.Daily.Select(d => d.Counts.Total));
        Assert.Equal(4, report.Daily[1].Counts.Completed);
    }

    [Fact]
    public async Task Summary_DefaultsToTheCurrentMonth_AndRejectsInvalidRanges()
    {
        var s = await Scenario();
        LogInAsReporter(s);
        var service = s.Ctx.CreateReportService();

        var current = await service.SummaryAsync(new(), CancellationToken.None);
        var reversed = await service.SummaryAsync(new(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 1)), CancellationToken.None);
        var tooLong = await service.SummaryAsync(new(new DateOnly(2025, 11, 1), new DateOnly(2026, 11, 13)), CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 13)), (current.Value.From, current.Value.To));
        Assert.Equal(13, current.Value.Daily.Count);
        Assert.Equal(ErrorType.Validation, reversed.Error!.Type);
        Assert.Contains("to", reversed.Error.FieldErrors!.Keys);
        Assert.Equal(ErrorType.Validation, tooLong.Error!.Type);
    }

    [Fact]
    public async Task DepartmentHeads_SeeOnlyTheirDepartment_AndCannotAskForAnother()
    {
        var s = await Scenario();
        s.LogInAs(s.Noi, SystemRoles.DepartmentHead, Permissions.ReportsRead);
        var service = s.Ctx.CreateReportService();

        var own = await service.SummaryAsync(new(new DateOnly(2026, 11, 1)), CancellationToken.None);
        var other = await service.SummaryAsync(new(new DateOnly(2026, 11, 1), DepartmentId: s.Kkb.Id), CancellationToken.None);

        Assert.Equal(2, own.Value.Totals.Total);
        Assert.Equal(["NOI"], own.Value.ByDepartment.Select(d => d.Code));
        Assert.Equal(ErrorType.Forbidden, other.Error!.Type);
        Assert.Equal("reports.department_forbidden", other.Error.Code);
    }

    [Fact]
    public async Task Dashboard_ShowsCountsWorkWaitingForMeAndTheLast14Days()
    {
        var ct = CancellationToken.None;
        var s = await Scenario();
        // Phiếu CK-01 người bệnh đã ký, chờ tiếp đón xác nhận; một phiếu CK-01 chờ người bệnh ký.
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var waiting = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct)).Value.Id;
        await s.Service.UpdateAsync(waiting, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), ct);
        await s.Service.SubmitAsync(waiting, ct);
        await s.Signing.StartSigningAsync(waiting, ct);
        var back = s.UseSigningToken();
        await s.Signing.SignAsPatientAsync(waiting, new(ConsentDecision.Agreed, SignaturePng), ct);
        back();
        var notSigned = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct)).Value.Id;
        await s.Service.UpdateAsync(notSigned, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), ct);
        await s.Service.SubmitAsync(notSigned, ct);

        var dashboard = await s.Ctx.CreateDashboardService().GetAsync(ct);

        // Khoa Khám bệnh: 1 nháp từ kịch bản, 2 phiếu chờ ký mới.
        Assert.Equal(1, dashboard.Drafts);
        Assert.Equal(2, dashboard.PendingSignature);
        // Phiếu thứ tư của kịch bản hoàn tất lúc 17:00 hôm nay.
        Assert.Equal(1, dashboard.CompletedToday);
        Assert.Equal(waiting, Assert.Single(dashboard.AwaitingMyConfirmation.Items).Id);
        Assert.Equal(1, dashboard.AwaitingMyConfirmation.Total);
        Assert.Equal(2, dashboard.MyOpenForms.Total);
        Assert.DoesNotContain(dashboard.MyOpenForms.Items, f => f.Status == ConsentFormStatus.Completed);
        Assert.Equal(DashboardService.ChartDays, dashboard.Last14Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 13), dashboard.Last14Days[^1].Date);
        Assert.Equal(2, dashboard.Last14Days[^1].Created);
        var nov11 = dashboard.Last14Days.Single(d => d.Date == new DateOnly(2026, 11, 11));
        Assert.Equal(5, nov11.Created);
        Assert.Equal(3, nov11.Completed);
        Assert.Equal(1, dashboard.Last14Days.Single(d => d.Date == new DateOnly(2026, 11, 13)).Completed);

        // Điều dưỡng Khoa Nội không có vai trò xác nhận CK-01 và không thấy phiếu của Khoa Khám bệnh.
        s.LogIn(s.Noi);
        var nurse = await s.Ctx.CreateDashboardService().GetAsync(ct);
        Assert.Equal(0, nurse.AwaitingMyConfirmation.Total);
        Assert.Equal(1, nurse.Drafts);
        Assert.Equal(0, nurse.PendingSignature);
    }
}
