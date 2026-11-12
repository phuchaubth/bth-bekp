using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Shared;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

internal sealed class ConsentTemplateRepository(ConsentDbContext db) : IConsentTemplateRepository
{
    public async Task<IReadOnlyList<ConsentTemplate>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.ConsentTemplates.Include(t => t.Versions)
            .Where(t => includeInactive || t.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<ConsentTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.ConsentTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.ConsentTemplates.AnyAsync(t => t.Code == code, cancellationToken);

    public Task<ConsentTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken) =>
        db.Set<ConsentTemplateVersion>().FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

    public void Add(ConsentTemplate template) => db.ConsentTemplates.Add(template);
}

internal sealed class ConsentFormRepository(ConsentDbContext db) : IConsentFormRepository
{
    public Task<ConsentForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.ConsentForms.Include(f => f.Signatures).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<ConsentFormRef?> FindReplacementAsync(Guid formId, CancellationToken cancellationToken) =>
        db.ConsentForms.AsNoTracking()
            .Where(f => f.ReplacesFormId == formId)
            .Select(f => new ConsentFormRef(f.Id, f.FormNo))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<ConsentFormListItemDto>> SearchAsync(ConsentFormSearch query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows =
            from f in db.ConsentForms.AsNoTracking()
            join p in db.Patients on f.PatientId equals p.Id
            join t in db.ConsentTemplates on f.TemplateId equals t.Id
            join v in db.Set<ConsentTemplateVersion>() on f.TemplateVersionId equals v.Id
            join d in db.Departments on f.DepartmentId equals d.Id
            join u in db.Users on f.CreatedByUserId equals u.Id
            select new { Form = f, Patient = p, Template = t, Version = v, Department = d, Creator = u };

        if (!query.ScopeAll)
        {
            rows = rows.Where(r => r.Form.DepartmentId == query.ScopeDepartmentId || r.Form.CreatedByUserId == query.ScopeUserId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(r => r.Form.Status == status);
        }

        if (query.TemplateId is { } templateId)
        {
            rows = rows.Where(r => r.Form.TemplateId == templateId);
        }

        if (query.PatientId is { } patientId)
        {
            rows = rows.Where(r => r.Form.PatientId == patientId);
        }

        if (query.DepartmentId is { } departmentId)
        {
            rows = rows.Where(r => r.Form.DepartmentId == departmentId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            rows = rows.Where(r => r.Form.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            rows = rows.Where(r => r.Form.CreatedAtUtc < toUtc);
        }

        if (query.Keyword is { } keyword)
        {
            // Họ tên dùng collation không phân biệt dấu (tìm "pham thi dung" khớp "Phạm Thị Dung").
            rows = rows.Where(r =>
                r.Form.FormNo.Contains(keyword) || r.Patient.FullName.Contains(keyword) || r.Patient.PatientCode == keyword);
        }

        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows
            .OrderByDescending(r => r.Form.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new ConsentFormListItemDto(
                r.Form.Id,
                r.Form.FormNo,
                r.Form.Status,
                r.Template.Id,
                r.Template.Code,
                r.Template.Name,
                r.Version.VersionNo,
                r.Patient.Id,
                r.Patient.FullName,
                r.Patient.PatientCode,
                r.Department.Id,
                r.Department.Name,
                r.Creator.FullName,
                r.Form.CreatedAtUtc,
                r.Form.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<ConsentFormListItemDto>(items, query.Page, query.PageSize, total);
    }

    public void Add(ConsentForm form) => db.ConsentForms.Add(form);
}

/// <summary>Số phiếu <c>PDT-{năm}-{số thứ tự}</c>, số thứ tự lấy từ sequence của SQL Server (không trùng kể cả khi lập đồng thời).</summary>
internal sealed class ConsentFormNumberGenerator(ConsentDbContext db) : IConsentFormNumberGenerator
{
    public const string SequenceName = "ConsentFormNumbers";

    public async Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // Không ghép thêm toán tử LINQ (như SingleAsync): EF sẽ bọc câu lệnh thành truy vấn con, nơi SQL Server
        // không cho phép NEXT VALUE FOR.
        var values = await db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR dbo.ConsentFormNumbers AS [Value]")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var next = values.Single();
        var year = nowUtc.ToOffset(ConsentFormService.VietnamOffset).Year;
        return $"PDT-{year}-{next:000000}";
    }
}
