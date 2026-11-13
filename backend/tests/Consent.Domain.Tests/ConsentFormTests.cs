using Consent.Domain.Common;
using Consent.Domain.ConsentForms;

namespace Consent.Domain.Tests;

public sealed class ConsentFormTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private const string Reason = "Lập nhầm người bệnh";

    public enum FormAction
    {
        Edit,
        Submit,
        ReturnToDraft,
        Cancel,
        StartSigning,
        PatientSign,
        Complete,
        Revoke,
    }

    /// <summary>Trạng thái phiếu, tách "chờ ký" thành chờ người bệnh ký và chờ phía bệnh viện xác nhận.</summary>
    public enum Stage
    {
        Draft,
        AwaitingPatient,
        AwaitingHospital,
        Completed,
        Cancelled,
        Revoked,
    }

    private static readonly Guid Session = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static ConsentForm NewForm() =>
        ConsentForm.Create("PDT-2026-000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", Guid.NewGuid(), Now);

    private static void PatientSigns(ConsentForm form) =>
        form.SignByPatient(Session, ConsentDecision.Agreed, "Người bệnh giả lập", Png, "Thiết bị thử", Guid.NewGuid(), Now);

    private static void HospitalConfirms(ConsentForm form) =>
        form.Complete(Guid.NewGuid(), "Nhân viên giả lập", "Điều dưỡng", "2026/11/a.pdf.enc", new string('a', 64), 1000, Now);

    /// <summary>Đưa phiếu mới tới trạng thái cần kiểm thử bằng các bước hợp lệ.</summary>
    private static ConsentForm FormIn(Stage stage)
    {
        var form = NewForm();
        if (stage is Stage.AwaitingPatient or Stage.AwaitingHospital or Stage.Completed or Stage.Revoked)
        {
            form.Submit("{}", "{\"patient\":{}}", Now);
            form.StartSigning(Session, Now);
        }

        if (stage is Stage.AwaitingHospital or Stage.Completed or Stage.Revoked)
        {
            PatientSigns(form);
        }

        if (stage is Stage.Completed or Stage.Revoked)
        {
            HospitalConfirms(form);
        }

        if (stage == Stage.Revoked)
        {
            form.Revoke(Reason, Now);
        }

        if (stage == Stage.Cancelled)
        {
            form.Cancel(Reason, Now);
        }

        Assert.Equal(stage, StageOf(form));
        return form;
    }

    private static Stage StageOf(ConsentForm form) => form.Status switch
    {
        ConsentFormStatus.Draft => Stage.Draft,
        ConsentFormStatus.PendingSignature => form.PatientSignature is null ? Stage.AwaitingPatient : Stage.AwaitingHospital,
        ConsentFormStatus.Completed => Stage.Completed,
        ConsentFormStatus.Cancelled => Stage.Cancelled,
        _ => Stage.Revoked,
    };

    private static void Apply(ConsentForm form, FormAction action)
    {
        switch (action)
        {
            case FormAction.Edit:
                form.UpdateDraft("{\"a\":1}", ConsentSignerKind.Patient, null, Now);
                break;
            case FormAction.Submit:
                form.Submit("{}", "{\"patient\":{}}", Now);
                break;
            case FormAction.ReturnToDraft:
                form.ReturnToDraft(Now);
                break;
            case FormAction.Cancel:
                form.Cancel(Reason, Now);
                break;
            case FormAction.StartSigning:
                form.StartSigning(Session, Now);
                break;
            case FormAction.PatientSign:
                PatientSigns(form);
                break;
            case FormAction.Complete:
                HospitalConfirms(form);
                break;
            case FormAction.Revoke:
                form.Revoke(Reason, Now);
                break;
        }
    }

    /// <summary>Bảng chuyển trạng thái đầy đủ: (trạng thái, thao tác) → trạng thái sau, hoặc null nếu không hợp lệ.</summary>
    public static TheoryData<Stage, FormAction, Stage?> Transitions()
    {
        var data = new TheoryData<Stage, FormAction, Stage?>();
        var allowed = new Dictionary<(Stage, FormAction), Stage>
        {
            [(Stage.Draft, FormAction.Edit)] = Stage.Draft,
            [(Stage.Draft, FormAction.Submit)] = Stage.AwaitingPatient,
            [(Stage.Draft, FormAction.Cancel)] = Stage.Cancelled,
            [(Stage.AwaitingPatient, FormAction.ReturnToDraft)] = Stage.Draft,
            [(Stage.AwaitingPatient, FormAction.Cancel)] = Stage.Cancelled,
            [(Stage.AwaitingPatient, FormAction.StartSigning)] = Stage.AwaitingPatient,
            [(Stage.AwaitingPatient, FormAction.PatientSign)] = Stage.AwaitingHospital,
            [(Stage.AwaitingHospital, FormAction.ReturnToDraft)] = Stage.Draft,
            [(Stage.AwaitingHospital, FormAction.Cancel)] = Stage.Cancelled,
            [(Stage.AwaitingHospital, FormAction.Complete)] = Stage.Completed,
            [(Stage.Completed, FormAction.Revoke)] = Stage.Revoked,
        };
        foreach (var stage in Enum.GetValues<Stage>())
        {
            foreach (var action in Enum.GetValues<FormAction>())
            {
                data.Add(stage, action, allowed.TryGetValue((stage, action), out var next) ? next : null);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void StateMachine_AllowsOnlyDefinedTransitions(Stage from, FormAction action, Stage? expected)
    {
        var form = FormIn(from);

        if (expected is null)
        {
            Assert.Throws<DomainException>(() => Apply(form, action));
            Assert.Equal(from, StageOf(form));
        }
        else
        {
            Apply(form, action);
            Assert.Equal(expected, StageOf(form));
        }
    }

    [Fact]
    public void PatientSignature_NeedsTheCurrentSession_AndCanBeUsedOnce()
    {
        var form = FormIn(Stage.AwaitingPatient);
        var other = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            form.SignByPatient(other, ConsentDecision.Agreed, "Người bệnh giả lập", Png, null, null, Now));

        // Mở phiên mới thì phiên cũ hết hiệu lực.
        form.StartSigning(other, Now);
        Assert.Throws<DomainException>(() => PatientSigns(form));

        form.SignByPatient(other, ConsentDecision.Refused, "Người bệnh giả lập", Png, "Thiết bị thử", null, Now);
        Assert.Null(form.SigningSessionId);
        Assert.Equal(ConsentDecision.Refused, form.Decision);
        Assert.Equal(ConsentSignerRole.Patient, form.PatientSignature!.Role);
        Assert.Equal(ConsentSignatureMethod.Handwritten, form.PatientSignature.Method);
        Assert.Throws<DomainException>(() =>
            form.SignByPatient(other, ConsentDecision.Agreed, "Người bệnh giả lập", Png, null, null, Now));
    }

    [Fact]
    public void PatientSignature_WithoutDecision_ForConfirmOnlyForms_AndRejectsUnknownDecision()
    {
        var form = FormIn(Stage.AwaitingPatient);

        Assert.Throws<DomainException>(() =>
            form.SignByPatient(Session, (ConsentDecision)99, "Người bệnh giả lập", Png, null, null, Now));
        form.SignByPatient(Session, null, "Người bệnh giả lập", Png, "Thiết bị thử", null, Now);
        HospitalConfirms(form);

        Assert.Null(form.Decision);
        Assert.Equal(ConsentFormStatus.Completed, form.Status);
    }

    [Fact]
    public void RepresentativeSigns_AsRepresentative_AndHospitalConfirmsByAccount()
    {
        var form = NewForm();
        form.UpdateDraft("{}", ConsentSignerKind.Representative, Guid.NewGuid(), Now);
        form.Submit("{}", "{\"patient\":{}}", Now);
        form.StartSigning(Session, Now);
        PatientSigns(form);
        HospitalConfirms(form);

        Assert.Equal(ConsentSignerRole.Representative, form.PatientSignature!.Role);
        var hospital = form.HospitalSignature!;
        Assert.Equal(ConsentSignatureMethod.AccountConfirmation, hospital.Method);
        Assert.Null(hospital.ImagePng);
        Assert.Equal("Điều dưỡng", hospital.SignerTitle);
        Assert.Equal(new string('a', 64), form.SignedPdfSha256);
        Assert.Equal(Now, form.CompletedAtUtc);
    }

    [Fact]
    public void ReturnToDraft_AfterPatientSigned_DropsTheSignature()
    {
        var form = FormIn(Stage.AwaitingHospital);

        form.ReturnToDraft(Now);

        Assert.Empty(form.Signatures);
        Assert.Null(form.Decision);
        Assert.Null(form.SigningSessionId);
    }

    [Fact]
    public void Submit_FreezesSnapshot_AndReturnToDraftClearsIt()
    {
        var form = NewForm();

        form.Submit("{}", "{\"patient\":{\"fullName\":\"Giả lập\"}}", Now);
        Assert.NotNull(form.SnapshotJson);
        Assert.Equal(Now, form.SubmittedAtUtc);

        form.ReturnToDraft(Now);
        Assert.Null(form.SnapshotJson);
        Assert.Null(form.SubmittedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ngắn")]
    public void CancelAndRevoke_RequireAMeaningfulReason(string reason)
    {
        Assert.Throws<DomainException>(() => NewForm().Cancel(reason, Now));
        Assert.Throws<DomainException>(() => FormIn(Stage.Completed).Revoke(reason, Now));
    }

    [Fact]
    public void RepresentativeSigner_RequiresRepresentative_AndPatientSignerClearsIt()
    {
        var form = NewForm();
        Assert.Throws<DomainException>(() => form.UpdateDraft("{}", ConsentSignerKind.Representative, null, Now));

        var representativeId = Guid.NewGuid();
        form.UpdateDraft("{}", ConsentSignerKind.Representative, representativeId, Now);
        Assert.Equal(representativeId, form.RepresentativeId);

        form.UpdateDraft("{}", ConsentSignerKind.Patient, representativeId, Now);
        Assert.Null(form.RepresentativeId);
    }
}
