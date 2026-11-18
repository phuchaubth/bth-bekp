namespace BEKP.Domain.Common;

/// <summary>Base type for aggregates. UUID primary keys are internal; business codes are separate fields.</summary>
public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Id must not be empty.");
        }

        Id = id;
    }

    public Guid Id { get; protected set; }
}
