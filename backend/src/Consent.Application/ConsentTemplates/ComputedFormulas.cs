using System.Globalization;

namespace Consent.Application.ConsentTemplates;

/// <summary>
/// Công thức của trường tự tính (khối <c>computed</c>). Máy chủ luôn tự tính lại khi lưu và trình ký, bỏ qua giá trị
/// người dùng gửi lên. Cùng công thức với frontend/src/features/consents/templateLogic.ts.
/// </summary>
public static class ComputedFormulas
{
    /// <summary>BMI = cân nặng (kg) / chiều cao (m)²; đầu vào: [cân nặng kg, chiều cao cm]; làm tròn 1 chữ số thập phân.</summary>
    public const string Bmi = "bmi";

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Số trường đầu vào của từng công thức.</summary>
    public static IReadOnlyDictionary<string, int> InputCounts { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal) { [Bmi] = 2 };

    /// <summary>Kết quả dạng chữ (dấu phẩy thập phân, ví dụ "21,5"); thiếu hoặc sai đầu vào thì trả về null.</summary>
    public static string? Compute(string formula, IReadOnlyList<string?> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        switch (formula)
        {
            case Bmi when inputs.Count == 2 && Parse(inputs[0]) is { } weightKg && Parse(inputs[1]) is { } heightCm:
                if (weightKg <= 0 || heightCm <= 0)
                {
                    return null;
                }

                var heightM = heightCm / 100m;
                var bmi = Math.Round(weightKg / (heightM * heightM), 1, MidpointRounding.AwayFromZero);
                return bmi.ToString("0.0", Vietnamese);
            default:
                return null;
        }
    }

    /// <summary>Số nhập tay: chấp nhận dấu phẩy hoặc dấu chấm thập phân ("58,5", "58.5").</summary>
    private static decimal? Parse(string? text) =>
        decimal.TryParse(
            text?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
