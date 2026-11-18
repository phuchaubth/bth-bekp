using Consent.Domain.Common;
using Consent.Domain.ConsentTemplates;

namespace Consent.Domain.Tests;

public sealed class ConsentTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private static ConsentTemplate NewTemplate() =>
        ConsentTemplate.Create(" ck-01 ", "Giấy cam kết", "receptionist", null, "{\"v\":1}", Now);

    [Fact]
    public void Create_NormalizesCodeAndRole_AndStartsWithDraftVersion1()
    {
        var template = NewTemplate();

        Assert.Equal("CK-01", template.Code);
        Assert.Equal("RECEPTIONIST", template.HospitalSignerRoleCode);
        var version = Assert.Single(template.Versions);
        Assert.Equal(1, version.VersionNo);
        Assert.Equal(TemplateVersionStatus.Draft, version.Status);
        Assert.Null(template.PublishedVersion);
    }

    [Fact]
    public void Publish_Draft_MakesItTheOnlyPublishedVersion()
    {
        var template = NewTemplate();
        template.Publish(1, Now);

        var draft = template.CreateDraftFromLatest(Now.AddDays(1));
        template.UpdateDraft(draft.VersionNo, "{\"v\":2}", Now.AddDays(1));
        template.Publish(2, Now.AddDays(2));

        Assert.Equal(2, template.PublishedVersion!.VersionNo);
        Assert.Equal(TemplateVersionStatus.Retired, template.FindVersion(1)!.Status);
        Assert.Equal(Now.AddDays(2), template.FindVersion(1)!.RetiredAtUtc);
        Assert.Single(template.Versions, v => v.Status == TemplateVersionStatus.Published);
    }

    [Fact]
    public void CreateDraft_CopiesLatestContent_AndOnlyOneDraftAtATime()
    {
        var template = NewTemplate();
        template.Publish(1, Now);

        var draft = template.CreateDraftFromLatest(Now);

        Assert.Equal(2, draft.VersionNo);
        Assert.Equal("{\"v\":1}", draft.ContentJson);
        Assert.False(template.CanCreateDraft);
        Assert.Throws<DomainException>(() => template.CreateDraftFromLatest(Now));
    }

    [Fact]
    public void PublishedVersion_CannotBeEdited_OrPublishedAgain()
    {
        var template = NewTemplate();
        template.Publish(1, Now);

        Assert.Throws<DomainException>(() => template.UpdateDraft(1, "{}", Now));
        Assert.Throws<DomainException>(() => template.Publish(1, Now));
    }

    [Fact]
    public void Retire_OnlyPublishedVersion()
    {
        var template = NewTemplate();
        Assert.Throws<DomainException>(() => template.Retire(1, Now));

        template.Publish(1, Now);
        template.Retire(1, Now);

        Assert.Null(template.PublishedVersion);
        Assert.Equal(TemplateVersionStatus.Retired, template.FindVersion(1)!.Status);
    }

    [Fact]
    public void UnknownVersion_Throws()
    {
        var template = NewTemplate();

        Assert.Throws<DomainException>(() => template.Publish(9, Now));
    }
}
