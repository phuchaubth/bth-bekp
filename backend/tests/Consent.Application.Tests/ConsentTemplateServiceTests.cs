using System.Text.Json;
using Consent.Application.ConsentTemplates;
using Consent.Application.Tests.Fakes;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ConsentTemplateServiceTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Create_StartsWithValidDraft_AndRejectsDuplicateCode()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateConsentTemplateService();

        var created = await service.CreateAsync(new("tv-01", "Phiếu thử nghiệm", SystemRoles.Doctor, null), CancellationToken.None);
        var duplicate = await service.CreateAsync(new("TV-01", "Trùng mã", SystemRoles.Doctor, null), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("TV-01", created.Value.Code);
        Assert.Equal(TemplateVersionStatus.Draft, Assert.Single(created.Value.Versions).Status);
        Assert.Equal("consent_templates.duplicate_code", duplicate.Error!.Code);
    }

    [Fact]
    public async Task Create_WithUnknownRole_IsValidationError()
    {
        var ctx = new AppTestContext();

        var result = await ctx.CreateConsentTemplateService()
            .CreateAsync(new("TV-02", "Phiếu", "KHONG_CO", null), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("hospitalSignerRoleCode", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task UpdateDraft_InvalidDefinition_ReturnsMessages_AndKeepsOldContent()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateConsentTemplateService();
        var template = (await service.CreateAsync(new("TV-03", "Phiếu", SystemRoles.Nurse, null), CancellationToken.None)).Value;
        var before = ctx.Store.ConsentTemplates.Single().FindVersion(1)!.ContentJson;

        var result = await service.UpdateDraftAsync(
            template.Id, 1, new(Json("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"a\",\"label\":\"A\"}]}]}")),
            CancellationToken.None);

        Assert.Equal("consent_templates.invalid_definition", result.Error!.Code);
        Assert.Contains(result.Error!.FieldErrors!["definition"], m => m.Contains("ít nhất một lựa chọn", StringComparison.Ordinal));
        Assert.Equal(before, ctx.Store.ConsentTemplates.Single().FindVersion(1)!.ContentJson);
    }

    [Fact]
    public async Task PublishFlow_NewDraftThenPublish_RetiresPreviousVersion()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateConsentTemplateService();
        var template = (await service.CreateAsync(new("TV-04", "Phiếu", SystemRoles.Nurse, null), CancellationToken.None)).Value;

        await service.PublishAsync(template.Id, 1, CancellationToken.None);
        var draft = await service.CreateDraftAsync(template.Id, CancellationToken.None);
        var secondDraft = await service.CreateDraftAsync(template.Id, CancellationToken.None);
        var editPublished = await service.UpdateDraftAsync(template.Id, 1, new(Json("{}")), CancellationToken.None);
        var published = await service.PublishAsync(template.Id, 2, CancellationToken.None);

        Assert.Equal(2, draft.Value.VersionNo);
        Assert.Equal("consent_templates.draft_exists", secondDraft.Error!.Code);
        Assert.Equal("consent_templates.not_draft", editPublished.Error!.Code);
        Assert.Equal(TemplateVersionStatus.Published, published.Value.Versions.Single(v => v.VersionNo == 2).Status);
        Assert.Equal(TemplateVersionStatus.Retired, published.Value.Versions.Single(v => v.VersionNo == 1).Status);
    }

    [Fact]
    public async Task Available_OnlyPublishedActiveTemplates_ForThatDepartment()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateConsentTemplateService();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var kkb = ctx.AddDepartment("KKB", "Khoa Khám bệnh");
        var hospitalWide = (await service.CreateAsync(new("TV-10", "Toàn viện", SystemRoles.Nurse, null), CancellationToken.None)).Value;
        var noiOnly = (await service.CreateAsync(new("TV-11", "Chỉ khoa Nội", SystemRoles.Nurse, noi.Id), CancellationToken.None)).Value;
        await service.CreateAsync(new("TV-12", "Chưa phát hành", SystemRoles.Nurse, null), CancellationToken.None);
        await service.PublishAsync(hospitalWide.Id, 1, CancellationToken.None);
        await service.PublishAsync(noiOnly.Id, 1, CancellationToken.None);

        var forNoi = await service.ListAvailableAsync(noi.Id, CancellationToken.None);
        var forKkb = await service.ListAvailableAsync(kkb.Id, CancellationToken.None);

        Assert.Equal(["TV-10", "TV-11"], forNoi.Select(t => t.TemplateCode));
        Assert.Equal(["TV-10"], forKkb.Select(t => t.TemplateCode));
    }
}
