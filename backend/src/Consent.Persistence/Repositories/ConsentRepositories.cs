using System.Runtime.CompilerServices;
using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.Exports;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Exports;
using Consent.Domain.Patients;
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
            from f in ConsentFormFilters.Apply(db, query)
            join p in db.Patients on f.PatientId equals p.Id
            join t in db.ConsentTemplates on f.TemplateId equals t.Id
            join v in db.Set<ConsentTemplateVersion>() on f.TemplateVersionId equals v.Id
            join d in db.Departments on f.DepartmentId equals d.Id
            join u in db.Users on f.CreatedByUserId equals u.Id
            select new { Form = f, Patient = p, Template = t, Version = v, Department = d, Creator = u };

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

    public Task<int> CountAsync(ConsentFormSearch query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ConsentFormFilters.Apply(db, query).CountAsync(cancellationToken);
    }

    public async IAsyncEnumerable<ConsentFormExportRecord> StreamForExportAsync(
        ConsentFormSearch query, bool includeData, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows =
            from f in ConsentFormFilters.Apply(db, query)
            join p in db.Patients on f.PatientId equals p.Id
            join t in db.ConsentTemplates on f.TemplateId equals t.Id
            join v in db.Set<ConsentTemplateVersion>() on f.TemplateVersionId equals v.Id
            join d in db.Departments on f.DepartmentId equals d.Id
            join u in db.Users on f.CreatedByUserId equals u.Id
            join r in db.Set<LegalRepresentative>() on f.RepresentativeId equals (Guid?)r.Id into representatives
            from r in representatives.DefaultIfEmpty()
            orderby f.CreatedAtUtc, f.FormNo
            select new ConsentFormExportRecord
            {
                Id = f.Id,
                FormNo = f.FormNo,
                Status = f.Status,
                TemplateCode = t.Code,
                TemplateName = t.Name,
                TemplateVersionId = v.Id,
                TemplateVersionNo = v.VersionNo,
                DepartmentName = d.Name,
                CreatedByName = u.FullName,
                CreatedAtUtc = f.CreatedAtUtc,
                SubmittedAtUtc = f.SubmittedAtUtc,
                CompletedAtUtc = f.CompletedAtUtc,
                CancelledAtUtc = f.CancelledAtUtc,
                CancelReason = f.CancelReason,
                RevokedAtUtc = f.RevokedAtUtc,
                RevokeReason = f.RevokeReason,
                Decision = f.Decision,
                SignerKind = f.SignerKind,
                PatientId = p.Id,
                PatientName = p.FullName,
                PatientCode = p.PatientCode,
                PatientDateOfBirth = p.DateOfBirth,
                PatientGender = p.Gender,
                PatientCitizenId = p.CitizenId,
                PatientPhoneNumber = p.PhoneNumber,
                PatientAddress = p.CurrentAddress ?? p.PermanentAddress,
                RepresentativeId = r == null ? null : r.Id,
                RepresentativeName = r == null ? null : r.FullName,
                RepresentativeRelationship = r == null ? null : r.Relationship,
                RepresentativeCitizenId = r == null ? null : r.CitizenId,
                RepresentativePhoneNumber = r == null ? null : r.PhoneNumber,
                SnapshotJson = f.SnapshotJson,
                PatientSignedAtUtc = f.Signatures.Where(s => (s.Role == ConsentSignerRole.Patient || s.Role == ConsentSignerRole.Representative))
                    .Select(s => (DateTimeOffset?)s.SignedAtUtc).FirstOrDefault(),
                HospitalSignerName = f.Signatures.Where(s => s.Role == ConsentSignerRole.Hospital)
                    .Select(s => s.SignerName).FirstOrDefault(),
                HospitalSignerTitle = f.Signatures.Where(s => s.Role == ConsentSignerRole.Hospital)
                    .Select(s => s.SignerTitle).FirstOrDefault(),
                ReplacesFormNo = db.ConsentForms.Where(o => o.Id == f.ReplacesFormId).Select(o => o.FormNo).FirstOrDefault(),
                SignedPdfSha256 = f.SignedPdfSha256,
                DataJson = includeData ? f.DataJson : null,
            };

        var page = rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).AsAsyncEnumerable();
        await foreach (var record in page.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    public void Add(ConsentForm form) => db.ConsentForms.Add(form);
}

/// <summary>Phạm vi khoa và bộ lọc phiếu dùng chung cho tìm kiếm, xuất dữ liệu và thống kê (không gồm phân trang).</summary>
internal static class ConsentFormFilters
{
    /// <summary>Họ tên dùng collation không phân biệt dấu (tìm "pham thi dung" khớp "Phạm Thị Dung").</summary>
    public static IQueryable<ConsentForm> Apply(ConsentDbContext db, ConsentFormSearch query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var forms = db.ConsentForms.AsNoTracking();
        if (!query.ScopeAll)
        {
            forms = forms.Where(f => f.DepartmentId == query.ScopeDepartmentId || f.CreatedByUserId == query.ScopeUserId);
        }

        if (query.Status is { } status)
        {
            forms = forms.Where(f => f.Status == status);
        }

        if (query.TemplateId is { } templateId)
        {
            forms = forms.Where(f => f.TemplateId == templateId);
        }

        if (query.PatientId is { } patientId)
        {
            forms = forms.Where(f => f.PatientId == patientId);
        }

        if (query.DepartmentId is { } departmentId)
        {
            forms = forms.Where(f => f.DepartmentId == departmentId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            forms = forms.Where(f => f.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            forms = forms.Where(f => f.CreatedAtUtc < toUtc);
        }

        if (query.Keyword is { } keyword)
        {
            forms = forms.Where(f => f.FormNo.Contains(keyword)
                || db.Patients.Any(p => p.Id == f.PatientId && (p.FullName.Contains(keyword) || p.PatientCode == keyword)));
        }

        return forms;
    }
}

internal sealed class ExportJobRepository(ConsentDbContext db) : IExportJobRepository
{
    public void Add(ExportJob job) => db.ExportJobs.Add(job);

    public async Task<PagedResult<ExportJobRecord>> SearchAsync(ExportJobSearch query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var jobs = db.ExportJobs.AsNoTracking();
        if (query.RequestedByUserId is { } userId)
        {
            jobs = jobs.Where(j => j.RequestedByUserId == userId);
        }

        var total = await jobs.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await (
                from j in jobs
                join u in db.Users on j.RequestedByUserId equals u.Id
                orderby j.RequestedAtUtc descending, j.Id descending
                select new ExportJobRecord(
                    j.Id, j.RequestedAtUtc, j.RequestedByUserId, u.FullName, j.Format, j.FiltersJson, j.Columns, j.IncludesContent,
                    j.IncludesSensitiveData, j.RowCount, j.FileName, j.FileSizeBytes, j.FileSha256, j.DurationMs))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<ExportJobRecord>(items, query.Page, query.PageSize, total);
    }
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
