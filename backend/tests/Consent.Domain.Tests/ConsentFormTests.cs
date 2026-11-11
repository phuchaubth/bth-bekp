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
        Complete,
        Revoke,
    }

    private static ConsentForm NewForm() =>
        ConsentForm.Create("PDT-2026-000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", Guid.NewGuid(), Now);

    /// <summary>Đưa phiếu mới tới trạng thái cần kiểm thử bằng các bước hợp lệ.</summary>
    private static ConsentForm FormIn(ConsentFormStatus status)
    {
        var form = NewForm();
        switch (status)
        {
            case ConsentFormStatus.PendingSignature:
                form.Submit("{}", "{\"patient\":{}}", Now);
                break;
            case ConsentFormStatus.Completed:
                form.Submit("{}", "{\"patient\":{}}", Now);
                form.Complete(Now);
                break;
            case ConsentFormStatus.Cancelled:
                form.Cancel(Reason, Now);
                break;
            case ConsentFormStatus.Revoked:
                form.Submit("{}", "{\"patient\":{}}", Now);
                form.Complete(Now);
                form.Revoke(Reason, Now);
                break;
        }

        Assert.Equal(status, form.Status);
        return form;
    }

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
            case FormAction.Complete:
                form.Complete(Now);
                break;
            case FormAction.Revoke:
                form.Revoke(Reason, Now);
                break;
        }
    }

    /// <summary>Bảng chuyển trạng thái đầy đủ: (trạng thái, thao tác) → trạng thái sau, hoặc null nếu không hợp lệ.</summary>
    public static TheoryData<ConsentFormStatus, FormAction, ConsentFormStatus?> Transitions()
    {
        var data = new TheoryData<ConsentFormStatus, FormAction, ConsentFormStatus?>();
        var allowed = new Dictionary<(ConsentFormStatus, FormAction), ConsentFormStatus>
        {
            [(ConsentFormStatus.Draft, FormAction.Edit)] = ConsentFormStatus.Draft,
            [(ConsentFormStatus.Draft, FormAction.Submit)] = ConsentFormStatus.PendingSignature,
            [(ConsentFormStatus.Draft, FormAction.Cancel)] = ConsentFormStatus.Cancelled,
            [(ConsentFormStatus.PendingSignature, FormAction.ReturnToDraft)] = ConsentFormStatus.Draft,
            [(ConsentFormStatus.PendingSignature, FormAction.Cancel)] = ConsentFormStatus.Cancelled,
            [(ConsentFormStatus.PendingSignature, FormAction.Complete)] = ConsentFormStatus.Completed,
            [(ConsentFormStatus.Completed, FormAction.Revoke)] = ConsentFormStatus.Revoked,
        };
        foreach (var status in Enum.GetValues<ConsentFormStatus>())
        {
            foreach (var action in Enum.GetValues<FormAction>())
            {
                data.Add(status, action, allowed.TryGetValue((status, action), out var next) ? next : null);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void StateMachine_AllowsOnlyDefinedTransitions(ConsentFormStatus from, FormAction action, ConsentFormStatus? expected)
    {
        var form = FormIn(from);

        if (expected is null)
        {
            Assert.Throws<DomainException>(() => Apply(form, action));
            Assert.Equal(from, form.Status);
        }
        else
        {
            Apply(form, action);
            Assert.Equal(expected, form.Status);
        }
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
        Assert.Throws<DomainException>(() => FormIn(ConsentFormStatus.Completed).Revoke(reason, Now));
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
