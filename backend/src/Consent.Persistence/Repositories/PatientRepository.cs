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

    /// <summary>
    /// Từ khóa là 12 chữ số → tìm chính xác theo số CCCD (qua chỉ mục mù); còn lại → tìm theo mã người bệnh
    /// (chính xác) hoặc họ tên (không phân biệt dấu).
    /// </summary>
    public async Task<PagedResult<PatientSummary>> SearchAsync(PatientQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var patients = db.Patients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword;
            if (CitizenIdNumber.HasValidFormat(keyword))
            {
                var hash = blindIndexer.Compute(keyword);
                patients = patients.Where(p => EF.Property<string?>(p, PatientConfiguration.CitizenIdHash) == hash);
            }
            else
            {
                patients = patients.Where(p => p.PatientCode == keyword || p.FullName.Contains(keyword));
            }
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
