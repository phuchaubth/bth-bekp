using Consent.Application.Abstractions;
using Consent.Domain.Testing;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence.Repositories;

internal sealed class TestAssignmentRepository(ConsentDbContext db) : ITestAssignmentRepository
{
    private const int MaxListed = 2000;

    public void Add(TestAssignment assignment) => db.TestAssignments.Add(assignment);

    public Task<TestAssignment?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.TestAssignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TestAssignment>> ListAsync(string? version, CancellationToken cancellationToken) =>
        await db.TestAssignments
            .Where(a => version == null || a.Version == version)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenBy(a => a.Feature)
            .Take(MaxListed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<TestAssignment>> ListForAssigneeAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.TestAssignments
            .Where(a => a.AssigneeUserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(MaxListed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        await db.TestAssignments
            .GroupBy(a => a.Version)
            .Select(g => new { Version = g.Key, Latest = g.Max(a => a.CreatedAtUtc) })
            .OrderByDescending(v => v.Latest)
            .Select(v => v.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
