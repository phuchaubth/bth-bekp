using Consent.Application.Auditing;
using Consent.Application.Auth;
using Consent.Application.Departments;
using Consent.Application.Patients;
using Consent.Application.Roles;
using Consent.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Consent.Application;

public static class DependencyInjection
{
    /// <summary>Đăng ký các use case (service) và validator của tầng Application.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuthService>();
        services.AddScoped<DepartmentService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<PatientService>();

        RegisterValidators(services);
        return services;
    }

    /// <summary>Đăng ký mọi <see cref="IValidator{T}"/> trong assembly này (không cần thêm thư viện DI của FluentValidation).</summary>
    private static void RegisterValidators(IServiceCollection services)
    {
        var validatorTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in validatorTypes)
        {
            foreach (var validatorInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddSingleton(validatorInterface, type);
            }
        }
    }
}
