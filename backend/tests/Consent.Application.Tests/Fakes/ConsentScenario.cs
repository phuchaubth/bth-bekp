using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;

namespace Consent.Application.Tests.Fakes;

/// <summary>Dữ liệu giả lập dùng chung cho test phiếu: 2 khoa, 1 người bệnh (có người đại diện), mẫu CK-01 và TN-01.</summary>
internal sealed class ConsentScenario
{
    public required AppTestContext Ctx { get; init; }

    public required Department Noi { get; init; }

    public required Department Kkb { get; init; }

    public required Patient Patient { get; init; }

    public required ConsentTemplate Ck01 { get; init; }

    public required ConsentTemplate Tn01 { get; init; }

    public ConsentFormService Service => Ctx.CreateConsentFormService();

    public ConsentSigningService Signing => Ctx.CreateConsentSigningService();

    /// <summary>Đăng nhập như một điều dưỡng của khoa <paramref name="department"/>.</summary>
    public User LogIn(Department department, params string[] extraPermissions) =>
        LogInAs(department, SystemRoles.Nurse, extraPermissions);

    /// <summary>Đăng nhập với vai trò <paramref name="roleCode"/> (quyền mặc định: xem, lập, ký phiếu).</summary>
    public User LogInAs(Department department, string roleCode, params string[] extraPermissions)
    {
        var user = Ctx.AddUser($"nv{Ctx.Store.Users.Count}", roleCode, departmentId: department.Id);
        user.UpdateProfile(user.FullName, null, null, "Chức danh giả lập", department.Id, null);
        Ctx.CurrentUser.UserId = user.Id;
        Ctx.CurrentUser.UserName = user.UserName;
        Ctx.CurrentUser.DepartmentId = department.Id;
        Ctx.CurrentUser.SigningFormId = null;
        Ctx.CurrentUser.SigningSessionId = null;
        Ctx.CurrentUser.Roles.Clear();
        Ctx.CurrentUser.Roles.Add(roleCode);
        Ctx.CurrentUser.Permissions.Clear();
        Ctx.CurrentUser.Permissions.UnionWith(
            [Permissions.ConsentsRead, Permissions.ConsentsCreate, Permissions.ConsentsSign, .. extraPermissions]);
        return user;
    }

    /// <summary>
    /// Dùng token phiên ký vừa cấp (chế độ ký trên máy tính bảng): không còn vai trò, quyền nào khác.
    /// Trả về hàm khôi phục phiên đăng nhập của nhân viên (như khi nhân viên đăng nhập lại để thoát chế độ ký).
    /// </summary>
    public Action UseSigningToken()
    {
        var claims = Ctx.Tokens.LastSigningClaims ?? throw new InvalidOperationException("Chưa mở phiên ký.");
        var roles = Ctx.CurrentUser.Roles.ToList();
        var permissions = Ctx.CurrentUser.Permissions.ToList();
        Ctx.CurrentUser.SigningFormId = claims.FormId;
        Ctx.CurrentUser.SigningSessionId = claims.SessionId;
        Ctx.CurrentUser.Roles.Clear();
        Ctx.CurrentUser.Permissions.Clear();
        return () =>
        {
            Ctx.CurrentUser.SigningFormId = null;
            Ctx.CurrentUser.SigningSessionId = null;
            Ctx.CurrentUser.Roles.UnionWith(roles);
            Ctx.CurrentUser.Permissions.UnionWith(permissions);
        };
    }

    public static ConsentScenario Create()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội tổng hợp");
        var kkb = ctx.AddDepartment("KKB", "Khoa Khám bệnh");
        var patient = Patient.Create(
            new PatientDetails(
                "Phạm Thị Dung (giả lập)", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", null, null,
                "Số 9 Đường Thử Nghiệm", null, "0900000125", "Kinh", null, "Việt Nam", "GD4790000000125", "BN000125"),
            ctx.Clock.UtcNow);
        patient.AddRepresentative(
            new RepresentativeDetails("Phạm Văn Chồng (giả lập)", RepresentativeRelationship.Spouse, "036085000777", null, Gender.Male, null, null),
            ctx.Clock.UtcNow);
        ctx.Store.Patients.Add(patient);

        ConsentTemplate Seed(string code)
        {
            var source = DefaultConsentTemplates.All.Single(t => t.Code == code);
            var template = ConsentTemplate.Create(code, source.Name, source.HospitalSignerRoleCode, null, source.ContentJson, ctx.Clock.UtcNow);
            template.Publish(1, ctx.Clock.UtcNow);
            ctx.Store.ConsentTemplates.Add(template);
            return template;
        }

        return new ConsentScenario { Ctx = ctx, Noi = noi, Kkb = kkb, Patient = patient, Ck01 = Seed("CK-01"), Tn01 = Seed("TN-01") };
    }

    public static JsonObject CompleteCk01() => new()
    {
        ["examType"] = "bhyt",
        ["insuranceNumber"] = "GD4790000000125",
        ["insuranceValidFrom"] = "2026-01-01",
        ["insuranceValidTo"] = "2026-12-31",
        ["primaryCareFacility"] = "Trạm Y tế Phường Mẫu",
        ["referralPaper"] = "no",
        ["acknowledged"] = true,
    };
}
