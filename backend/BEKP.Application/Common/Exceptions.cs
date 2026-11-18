namespace BEKP.Application.Common;

/// <summary>Input failed validation. Mapped to HTTP 400.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>A referenced resource does not exist. Mapped to HTTP 404.</summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string resource, string key)
        : base($"{resource} '{key}' was not found.")
    {
    }
}
