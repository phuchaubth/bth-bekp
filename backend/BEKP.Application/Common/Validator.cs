namespace BEKP.Application.Common;

/// <summary>Small collector for validation errors, thrown as <see cref="RequestValidationException"/>.</summary>
internal sealed class Validator
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public Validator Require(bool condition, string field, string message)
    {
        if (!condition)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = [];
                errors[field] = list;
            }

            list.Add(message);
        }

        return this;
    }

    public void ThrowIfInvalid()
    {
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
        }
    }
}
