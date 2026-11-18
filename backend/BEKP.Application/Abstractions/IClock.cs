namespace BEKP.Application.Abstractions;

/// <summary>Source of the current UTC time (abstracted for testability).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
