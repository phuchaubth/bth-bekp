namespace BEKP.Domain.Common;

/// <summary>Raised when a business rule of the domain model is violated.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
