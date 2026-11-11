namespace Consent.Domain.Common;

/// <summary>Che một phần dữ liệu nhạy cảm khi hiển thị, ví dụ <c>0790******12</c>.</summary>
public static class SensitiveMask
{
    public static string? Mask(string? value, int keepStart, int keepEnd)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length <= keepStart + keepEnd)
        {
            return new string('*', value.Length);
        }

        return string.Concat(value.AsSpan(0, keepStart), new string('*', value.Length - keepStart - keepEnd), value.AsSpan(value.Length - keepEnd));
    }

    public static string? CitizenId(string? value) => Mask(value, 4, 2);

    public static string? PhoneNumber(string? value) => Mask(value, 3, 2);

    public static string? HealthInsuranceNumber(string? value) => Mask(value, 2, 3);
}
