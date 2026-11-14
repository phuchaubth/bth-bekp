using Consent.Shared;
using FluentValidation;
using FluentValidation.Results;

namespace Consent.Application.Common;

public static class ValidationExtensions
{
    /// <summary>Kiểm tra dữ liệu; trả về lỗi Validation (kèm lỗi từng trường) hoặc <c>null</c> nếu hợp lệ.</summary>
    public static async Task<Error?> ValidateToErrorAsync<T>(
        this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var result = await validator.ValidateAsync(instance, cancellationToken).ConfigureAwait(false);
        return result.IsValid ? null : result.ToError();
    }

    public static Error ToError(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var fieldErrors = result.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return Error.Validation("validation", "Dữ liệu không hợp lệ.", fieldErrors);
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return string.Empty;
        }

        // "Roles[0]" / "Address.City" -> giữ nguyên phần sau, chỉ hạ chữ đầu mỗi đoạn.
        var parts = propertyName.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
            }
        }

        return string.Join('.', parts);
    }
}
