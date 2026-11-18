namespace BEKP.Domain.Common;

internal static class Guard
{
    public static string Text(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static DateTime Utc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainException($"{name} must be a UTC timestamp.");
        }

        return value;
    }
}
