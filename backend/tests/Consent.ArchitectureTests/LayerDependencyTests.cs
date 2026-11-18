using System.Reflection;

namespace Consent.ArchitectureTests;

/// <summary>
/// Kiểm tra quy tắc phụ thuộc của Clean Architecture dựa trên tham chiếu assembly đã biên dịch.
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "Consent.Domain";
    private const string Shared = "Consent.Shared";
    private const string Application = "Consent.Application";
    private const string Persistence = "Consent.Persistence";
    private const string Infrastructure = "Consent.Infrastructure";
    private const string Api = "Consent.Api";

    private static readonly Assembly DomainAssembly = typeof(Domain.Common.Entity).Assembly;
    private static readonly Assembly SharedAssembly = typeof(Shared.Result).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(Persistence.ConsentDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.DependencyInjection).Assembly;

    public static TheoryData<string, string[]> ForbiddenDependencies => new()
    {
        { Domain, [Shared, Application, Persistence, Infrastructure, Api] },
        { Shared, [Domain, Application, Persistence, Infrastructure, Api] },
        { Application, [Persistence, Infrastructure, Api] },
        { Persistence, [Infrastructure, Api] },
        { Infrastructure, [Persistence, Api] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Layer_DoesNotReferenceForbiddenLayers(string layer, string[] forbidden)
    {
        var references = ReferencedConsentAssemblies(Resolve(layer));

        var violations = references.Intersect(forbidden).ToArray();

        Assert.True(violations.Length == 0, $"{layer} không được phụ thuộc vào: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_HasNoDependencyOnAnyOtherProject()
    {
        Assert.Empty(ReferencedConsentAssemblies(DomainAssembly));
    }

    [Fact]
    public void Application_DependsOnlyOnDomainAndShared()
    {
        var references = ReferencedConsentAssemblies(ApplicationAssembly);

        Assert.All(references, name => Assert.Contains(name, new[] { Domain, Shared }));
    }

    private static Assembly Resolve(string layer) => layer switch
    {
        Domain => DomainAssembly,
        Shared => SharedAssembly,
        Application => ApplicationAssembly,
        Persistence => PersistenceAssembly,
        Infrastructure => InfrastructureAssembly,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    private static string[] ReferencedConsentAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Consent.", StringComparison.Ordinal))
            .ToArray();
}
