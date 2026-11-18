using Consent.Application.Abstractions;
using Consent.Application.Patients;
using Consent.Domain.Patients;
using Consent.Persistence.Configurations;
using Consent.Shared;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

internal sealed class PatientRepository(ConsentDbContext db, IBlindIndexer blindIndexer) : IPatientRepository
{
    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Patients.Include(p => p.Representatives).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Patient?> FindByCitizenIdAsync(string citizenId, CancellationToken cancellationToken)
    {
        var hash = blindIndexer.Compute(citizenId);
        return db.Patients.Include(p => p.Representatives)
            .FirstOrDefaultAsync(p => EF.Property<string?>(p, PatientConfiguration.CitizenIdHash) == hash, cancellationToken);
    }

    public Task<bool> PatientCodeExistsAsync(string patientCode, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Patients.AnyAsync(p => p.PatientCode == patientCode && p.Id != excludeId, cancellationToken);

    public Task<Patient?> FindByPatientCodeAsync(string patientCode, CancellationToken cancellationToken) =>
        db.Patients.Include(p => p.Representatives).FirstOrDefaultAsync(p => p.PatientCode == patientCode, cancellationToken);

    /// <summary>
    /// Từ khóa là 12 chữ số → tìm chính xác theo số CCCD (qua chỉ mục mù); còn lại → tìm theo mã người bệnh
    /// (chính xác) hoặc họ tên (không phân biệt dấu). Người không xem toàn viện (quyết định 41): danh sách và tìm theo họ tên
    /// chỉ gồm người bệnh trong phạm vi (<see cref="PatientScope"/>); tìm đúng số CCCD hoặc mã người bệnh thì ra mọi người bệnh.
    /// </summary>
    public async Task<PagedResult<PatientSummary>> SearchAsync(PatientQuery query, PatientScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        var patients = db.Patients.AsNoTracking();
        var departmentIds = scope.DepartmentIds;
        var userId = scope.UserId;

        if (!string.IsNullOrWhiteSpace(query.Keyword) && CitizenIdNumber.HasValidFormat(query.Keyword))
        {
            var hash = blindIndexer.Compute(query.Keyword);
            patients = patients.Where(p => EF.Property<string?>(p, PatientConfiguration.CitizenIdHash) == hash);
        }
        else if (scope.All)
        {
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                var keyword = query.Keyword;
                patients = patients.Where(p => p.PatientCode == keyword || p.FullName.Contains(keyword));
            }
        }
        else if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            patients = patients.Where(p => (userId != null && p.CreatedByUserId == userId)
                || db.ConsentForms.Any(f => f.PatientId == p.Id
                    && (departmentIds.Contains(f.DepartmentId) || (userId != null && f.CreatedByUserId == userId))));
        }
        else
        {
            // Mã người bệnh đúng thì ra mọi người bệnh; họ tên chỉ trong phạm vi.
            var keyword = query.Keyword;
            patients = patients.Where(p => p.PatientCode == keyword
                || (p.FullName.Contains(keyword)
                    && ((userId != null && p.CreatedByUserId == userId)
                        || db.ConsentForms.Any(f => f.PatientId == p.Id
                            && (departmentIds.Contains(f.DepartmentId) || (userId != null && f.CreatedByUserId == userId))))));
        }

        var total = await patients.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await patients
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PatientSummary(
                p.Id, p.PatientCode, p.FullName, p.DateOfBirth, p.Gender, p.CitizenId, p.HisVerifiedAtUtc, p.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<PatientSummary>(items, query.Page, query.PageSize, total);
    }

    public void Add(Patient patient) => db.Patients.Add(patient);
}
